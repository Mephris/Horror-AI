using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static Node;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.GraphView; // Required for Handles.Label
#endif

// =================================================================
// --- MEMORY DATA STRUCTURE ---
// =================================================================
[System.Serializable]
public class HunterPatrolMemory
{
    public Transform patrolpointTransform;
    public PointType pointType;

    public float lastPatrolTime = 0f;
    public float playerProbability = 0f;

    public bool hasHeardNoise = false;
    public bool hasDirectorTip = false;

    public bool IsWorthyOfInvestigation => hasHeardNoise || hasDirectorTip || playerProbability > 0.5f;

    public float calculatedPriorityScore = 0f;

    [System.NonSerialized] public RoomInfo parentRoom;
    [System.NonSerialized] public RoomInfo linkedRoomInfo;
}

// =================================================================
// --- MAIN AI CONTROLLER ---
// =================================================================
public class HunterAI : MonoBehaviour
{
    // --- Behavior Tree Fields ---
    private Node rootNode;
    private HunterBehaviorNodes btContext;

    // --- NAVIGATION & TARGETS (THE DATA SPLIT) ---
    public Transform currentInterestTarget = null;   // The object being stalked (e.g. Door, Table)
    public Vector3 currentNavDestination = Vector3.zero; // The floor coordinate to move to (Vantage Point)
    public Transform targetPos; // Chase target

    private NavMeshAgent agent;

    [HideInInspector] public Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();
    [HideInInspector] public Dictionary<string, RoomInfo> roomData = new Dictionary<string, RoomInfo>();

    // --- SETTINGS: Scoring & Brain ---
    [Header("Decision Scoring")]
    [Tooltip("Multiplier for points in the same room as the Hunter. Keeps him focused.")]
    [Range(1.0f, 2.0f)]
    public float sameRoomMultiplier = 1.1f;

    [Tooltip("How much Distance reduces the score. Higher = Lazier Hunter.")]
    public float distancePenalty = 1.0f;

    [Header("Probability Settings")]
    [HideInInspector] public float baseUncertainty = 0.2f;
    [SerializeField] private float probabilityUpdateInterval = 1f;
    private WaitForSeconds probabilityWait;

    // --- BEHAVIOR & COOLDOWN ---
    [Header("Behavior Settings")]
    [Tooltip("After peeking a door, how long until he allows himself to peek another.")]
    public float peekSkillCooldown = 15.0f;
    private float nextPeekTime = 0f; // Global cooldown timer

    // Track room history to prevent immediate backtracking
    private RoomInfo currentRoomInfo = null;
    private RoomInfo previousRoomInfo = null;

    // --- CHASE ---
    [Header("Chase Settings")]
    [SerializeField] public float chaseInvestigationTime = 7.0f;
    [HideInInspector] public float timeSinceLastSeen = 999.0f;
    [HideInInspector] public bool isChasingPlayer = false;

    // --- DIRECTOR ---
    [Header("Director Interaction")]
    [SerializeField] private float directorCommandPathCostThreshold = 40f;

    [Header("BT Debug")]
    [SerializeField] public string currentBTState = "Initializing";

    // --- MOVEMENT DYNAMICS (Wiggly Carrot) ---
    [Header("Movement Dynamics")]
    [Tooltip("How far ahead on the path the 'Ghost Target' is placed.")]
    public float pathLookAheadDistance = 4.0f;
    [Tooltip("How wide the Hunter weaves (Sine Wave Amplitude).")]
    public float driftAmplitude = 1.5f;
    [Tooltip("How fast the Hunter weaves (Sine Wave Frequency).")]
    public float driftFrequency = 1.0f;
    [Tooltip("Speed when peeking into a room.")]
    public float creepSpeed = 0.5f;
    [Tooltip("Distance to drift into the room while peeking.")]
    public float creepDistance = 1.5f;


    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rooms = FindObjectsOfType<Room>();
        PatrolPoints[] allPointsInScene = FindObjectsOfType<PatrolPoints>();
        probabilityWait = new WaitForSeconds(probabilityUpdateInterval);

        // Initialization Logic (Omitted for brevity, but includes memory and link setup)
        // ... (This section includes memory building and link assignment) ... 

        if (targetPos == null) targetPos = new GameObject("PlayerChaseTarget_Dynamic").transform;

        StartCoroutine(UpdateCuriosityRoutine());
        rootNode = SetupBehaviorTree();

        // Event Subscriptions
        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        // ... (other event subscriptions) ...
    }

    void Update()
    {
        if (rootNode != null) rootNode.Evaluate();
        // ... (Timer logic) ...
    }

    // =================================================================================
    // --- CONSOLIDATED NAVIGATION HELPERS (FROM AUDIT) ---
    // =================================================================================
    // 1. PATH VALIDITY & COST CHECK
    public bool IsPathValid(Vector3 target, out float cost)
    {
        NavMeshPath path = new NavMeshPath();
        if (agent.CalculatePath(target, path) && path.status == NavMeshPathStatus.PathComplete)
        {
            cost = CalculatePathCost(path);
            return true;
        }
        cost = float.PositiveInfinity;
        return false;
    }

    // 2. FLOOR SAMPLING & VALIDATION (The Lifted Target Fix)
    public bool GetValidFloorPosition(Vector3 targetPos, float radius, out Vector3 snappedPos)
    {
        Vector3 liftedTarget = targetPos + (Vector3.up * 0.2f);
        NavMeshHit hit;
        if (NavMesh.SamplePosition(liftedTarget, out hit, radius, NavMesh.AllAreas))
        {
            snappedPos = hit.position;
            return true;
        }
        snappedPos = targetPos;
        return false;
    }


    // =================================================================================
    // --- THE BRAIN: Get Best Next Point (Final Scoring Logic) ---
    // =================================================================================
    public Transform GetBestNextPoint(Vector3 currentPos, List<Transform> ignorePoints = null)
    {
        Transform bestCandidate = null;
        float bestScore = float.NegativeInfinity;

        // --- CONTEXT SETUP (Momentum Bridge Logic) ---
        RoomInfo primaryContext = null;
        RoomInfo secondaryContext = null;

        if (currentInterestTarget != null && patrolPointData.TryGetValue(currentInterestTarget, out HunterPatrolMemory currMem))
        {
            primaryContext = currMem.parentRoom;
            if (currMem.pointType == PointType.Doorway && currMem.linkedRoomInfo != null)
                secondaryContext = currMem.linkedRoomInfo;
        }

        foreach (var pair in patrolPointData)
        {
            Transform point = pair.Key;
            HunterPatrolMemory memory = pair.Value;

            // --- FILTERS ---
            if (memory.playerProbability <= baseUncertainty) continue;
            if (ignorePoints != null && ignorePoints.Contains(point)) continue;

            // 1. GLOBAL COOLDOWN FILTER (Hard Stop)
            if (memory.pointType == PointType.Doorway && Time.time < nextPeekTime)
            {
                continue; // SKIPPED - Cannot use this skill right now.
            }
            // 2. BACKTRACK FILTER
            else if (memory.pointType == PointType.Doorway && memory.linkedRoomInfo == previousRoomInfo)
            {
                continue; // SKIPPED - Just came from here.
            }

            // Path Calculation (Filter)
            float trueWalkingDistance = float.PositiveInfinity;
            if (!IsPathValid(point.position, out trueWalkingDistance)) continue;

            // --- SCORING FORMULA ---
            float score = 0f;

            // A. BASE HEAT (Room Heat or Point Heat)
            if (memory.pointType == PointType.Doorway && memory.linkedRoomInfo != null)
                score = memory.linkedRoomInfo.generalCuriosity * 100f;
            else
                score = memory.playerProbability * 100f;

            // B. MOMENTUM BONUS (Dual Context)
            bool appliedDoorBonus = false; // Used to prevent redundant stacking

            if (memory.parentRoom != null)
            {
                if (memory.parentRoom == primaryContext || memory.parentRoom == secondaryContext)
                {
                    score *= sameRoomMultiplier;
                }
            }

            // C. DISTANCE PENALTY & PRIORITY
            score -= (trueWalkingDistance * distancePenalty);
            if (memory.IsWorthyOfInvestigation) score += 50f;

            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = point;
            }
        }

        return bestCandidate;
    }

    // =================================================================================
    // --- UTILITY & HELPERS ---
    // =================================================================================
    public void RecordPatrolVisit(Transform point)
    {
        if (patrolPointData.TryGetValue(point, out HunterPatrolMemory memory))
        {
            // 1. Update Room History
            if (memory.parentRoom != null && memory.parentRoom != currentRoomInfo)
            {
                previousRoomInfo = currentRoomInfo;
                currentRoomInfo = memory.parentRoom;
            }

            // 2. Standard Cool Down
            memory.lastPatrolTime = Time.time;
            memory.playerProbability = baseUncertainty;
            memory.hasDirectorTip = false;
            memory.hasHeardNoise = false;
            if (memory.parentRoom != null) memory.parentRoom.UpdateGeneralCuriosity();

            // 3. COOLDOWN TRIGGER
            if (memory.pointType == PointType.Doorway)
            {
                nextPeekTime = Time.time + peekSkillCooldown;
            }
        }
    }

    public float CalculatePathCost(NavMeshPath path)
    {
        float cost = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            cost += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }
        return cost;
    }

    // [SetupBehaviorTree, UpdateCuriosityRoutine, Event Handlers omitted for brevity, but exist]
    // ...
}
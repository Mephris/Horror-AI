using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static Node;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
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

    // --- HEAD DYNAMICS (NEWLY REQUIRED FOR HUNTERHEAD.CS) ---
    [Header("Head Dynamics")]
    [Tooltip("Time between selecting new random gaze points.")]
    public float idleLookInterval = 2.0f;
    [Tooltip("Speed of head rotation when performing idle scan.")]
    public float idleHeadTurnSpeed = 2.0f;

    // --- Memory Data ---
    private Room[] rooms;


    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rooms = FindObjectsOfType<Room>();
        PatrolPoints[] allPointsInScene = FindObjectsOfType<PatrolPoints>();
        probabilityWait = new WaitForSeconds(probabilityUpdateInterval);

        // 2. Initialize Room Data Buckets
        foreach (Room room in rooms)
        {
            int exits = 1;
            RoomInfo newRoomInfo = new RoomInfo(room, exits);
            if (!roomData.ContainsKey(newRoomInfo.roomName))
            {
                roomData.Add(newRoomInfo.roomName, newRoomInfo);
            }
        }

        // 3. Sort Points into Rooms
        foreach (PatrolPoints point in allPointsInScene)
        {
            Room ownerRoom = null;
            if (point.manualRoomOwner != null)
                ownerRoom = point.manualRoomOwner;
            else
                ownerRoom = point.GetComponentInParent<Room>();

            if (ownerRoom != null && roomData.TryGetValue(ownerRoom.name, out RoomInfo ownerInfo))
            {
                if (!patrolPointData.ContainsKey(point.transform))
                {
                    HunterPatrolMemory newMemory = new HunterPatrolMemory
                    {
                        patrolpointTransform = point.transform,
                        pointType = point.pointType,
                        playerProbability = 0.5f,
                        lastPatrolTime = Time.time,
                        parentRoom = ownerInfo
                    };

                    point.runtimeMemory = newMemory;
                    patrolPointData.Add(point.transform, newMemory);
                    ownerInfo.patrolPoints.Add(newMemory);
                }
            }
            else
            {
                Debug.LogWarning($"[HunterAI] Point '{point.name}' has no Room! Assign 'Manual Room Owner' or parent it to a Room.");
            }
        }

        // 4. Link Doors
        foreach (var kvp in patrolPointData)
        {
            PatrolPoints pointScript = kvp.Key.GetComponent<PatrolPoints>();
            HunterPatrolMemory memory = kvp.Value;

            if (pointScript.pointType == PointType.Doorway && pointScript.linkedRoom != null)
            {
                if (roomData.TryGetValue(pointScript.linkedRoom.name, out RoomInfo targetRoomInfo))
                {
                    memory.linkedRoomInfo = targetRoomInfo;
                }
            }
        }

        if (targetPos == null) targetPos = new GameObject("PlayerChaseTarget_Dynamic").transform;

        StartCoroutine(UpdateCuriosityRoutine());
        rootNode = SetupBehaviorTree();

        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove += OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;
        Actions.HunterSawPatrolPoint += OnPatrolPointSeen;
    }

    void Update()
    {
        if (rootNode != null) rootNode.Evaluate();

        if (timeSinceLastSeen > 0.0f) timeSinceLastSeen += Time.deltaTime;

        if (isChasingPlayer && timeSinceLastSeen >= chaseInvestigationTime)
        {
            isChasingPlayer = false;
            timeSinceLastSeen = 999.0f;
        }
    }

    // =================================================================================
    // --- CONSOLIDATED NAVIGATION HELPERS ---
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

        // --- CONTEXT SETUP ---
        RoomInfo primaryContext = null;
        RoomInfo secondaryContext = null;

        if (currentInterestTarget != null && patrolPointData.TryGetValue(currentInterestTarget, out HunterPatrolMemory currMem))
        {
            primaryContext = currMem.parentRoom;
            if (currMem.pointType == PointType.Doorway && currMem.linkedRoomInfo != null)
            {
                secondaryContext = currMem.linkedRoomInfo;
            }
        }

        foreach (var pair in patrolPointData)
        {
            Transform point = pair.Key;
            HunterPatrolMemory memory = pair.Value;

            // --- FILTERS ---
            if (memory.playerProbability <= baseUncertainty) continue;
            if (ignorePoints != null && ignorePoints.Contains(point)) continue;

            // Path Calculation (Filter)
            float trueWalkingDistance = float.PositiveInfinity;
            if (!IsPathValid(point.position, out trueWalkingDistance)) continue;

            // --- BEHAVIOR FILTERS ---
            // 1. GLOBAL COOLDOWN FILTER (Hard Stop)
            if (memory.pointType == PointType.Doorway && Time.time < nextPeekTime)
            {
                continue;
            }
            // 2. BACKTRACK FILTER
            else if (memory.pointType == PointType.Doorway && memory.linkedRoomInfo == previousRoomInfo)
            {
                continue;
            }

            // --- SCORING FORMULA ---
            float score = 0f;

            // 1. BASE HEAT 
            if (memory.pointType == PointType.Doorway && memory.linkedRoomInfo != null)
                score = memory.linkedRoomInfo.generalCuriosity * 100f;
            else
                score = memory.playerProbability * 100f;

            // 2. MOMENTUM BONUS (Dual Context)
            if (memory.parentRoom != null)
            {
                if (memory.parentRoom == primaryContext || memory.parentRoom == secondaryContext)
                {
                    score *= sameRoomMultiplier;
                }
            }

            // 3. PENALTY & PRIORITY
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

    // NEW: Utility function for the HunterHeadController
    public List<HunterPatrolMemory> GetLocalHotPoints(Vector3 center, float radius)
    {
        return patrolPointData.Values
            .Where(p => p.playerProbability > baseUncertainty && Vector3.Distance(center, p.patrolpointTransform.position) <= radius)
            .OrderByDescending(p => p.playerProbability)
            .ToList();
    }


    private IEnumerator UpdateCuriosityRoutine()
    {
        while (true)
        {
            yield return probabilityWait;
            foreach (HunterPatrolMemory memory in patrolPointData.Values)
            {
                float timeSinceLastVisit = Time.time - memory.lastPatrolTime;
                if (timeSinceLastVisit > 30f)
                {
                    memory.playerProbability = Mathf.Min(0.5f, memory.playerProbability + 0.02f);
                }
            }
            foreach (RoomInfo room in roomData.Values)
            {
                room.UpdateGeneralCuriosity();
            }
        }
    }

    // --- Director Command Handlers (Needs updating to use IsPathValid) ---
    private void ModifyMemoryNearLocation(Vector3 location, float probabilityIncrease, bool setDirectorTip)
    {
        List<Transform> pointsToUpdate = new List<Transform>();
        NavMeshPath path = new NavMeshPath();

        foreach (var pair in patrolPointData)
        {
            if (NavMesh.CalculatePath(location, pair.Key.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                if (CalculatePathCost(path) <= directorCommandPathCostThreshold)
                    pointsToUpdate.Add(pair.Key);
            }
        }

        foreach (Transform t in pointsToUpdate)
        {
            HunterPatrolMemory mem = patrolPointData[t];
            mem.playerProbability = Mathf.Clamp01(mem.playerProbability + probabilityIncrease);
            if (setDirectorTip) mem.hasDirectorTip = true;
            if (mem.parentRoom != null) mem.parentRoom.UpdateGeneralCuriosity();
        }
    }

    private void OnCommandToMove(Vector3 target) => ModifyMemoryNearLocation(target, 0.2f, false);
    private void OnHighPriorityCommandToMove(Vector3 target) => ModifyMemoryNearLocation(target, 0.5f, true);

    private void OnSeePlayer(bool isVisible, Vector3 lastPlayerLocation)
    {
        if (isVisible)
        {
            targetPos.position = lastPlayerLocation;
            timeSinceLastSeen = 0.0f;
            isChasingPlayer = true;
            agent.isStopped = false;
        }
        else if (isChasingPlayer && timeSinceLastSeen == 0.0f)
        {
            timeSinceLastSeen = Time.deltaTime;
        }
    }

    private void OnPatrolPointSeen(Transform seenPointTransform)
    {
        if (patrolPointData.TryGetValue(seenPointTransform, out HunterPatrolMemory memory))
        {
            memory.lastPatrolTime = Time.time;
            if (memory.playerProbability > baseUncertainty)
            {
                float distance = Vector3.Distance(transform.position, seenPointTransform.position);
                float scale = Mathf.InverseLerp(25f, 5f, distance);
                float clearAmount = Mathf.Lerp(0.02f, 0.12f, scale);

                if (clearAmount > 0)
                    memory.playerProbability = Mathf.Max(baseUncertainty, memory.playerProbability - clearAmount);
            }
            if (memory.parentRoom != null) memory.parentRoom.UpdateGeneralCuriosity();
        }
    }

    // --- Behavior Tree Setup (Requires the class context) ---
    private Node SetupBehaviorTree()
    {
        btContext = new HunterBehaviorNodes(this, agent);

        // NODES
        var isPlayerSeen = new HunterBehaviorNodes.IsPlayerSeen(btContext);
        var chasePlayer = new HunterBehaviorNodes.ChasePlayer(btContext);
        var acquireTarget = new HunterBehaviorNodes.AcquirePatrolTarget(btContext);
        var movePatrol = new HunterBehaviorNodes.MoveToPatrolPoint(btContext);

        // CONTEXT ACTIONS
        var isDoorway = new HunterBehaviorNodes.IsPatrolPointType(btContext, PointType.Doorway);
        var peekAction = new HunterBehaviorNodes.PerformDoorwayPeek(btContext);
        var standardAction = new HunterBehaviorNodes.PerformStandardAction(btContext);

        // BRANCHES
        var chaseBranch = new Sequence("CHASE LOGIC", "High Priority: If player is visible, chase.", new List<Node> { isPlayerSeen, chasePlayer });

        var actionSelector = new Selector("CONTEXT ACTION", "Decides behavior based on point type.", new List<Node>
        {
            new Sequence("DOOR BEHAVIOR", "Peek into the room.", new List<Node> { isDoorway, peekAction }),
            standardAction
        });

        var patrolBranch = new Sequence("PATROL LOOP", "Find target, move, perform action.", new List<Node> { acquireTarget, movePatrol, actionSelector });

        return new Selector("ROOT AI", "Main Brain.", new List<Node> { chaseBranch, patrolBranch });
    }

    void OnDrawGizmos()
    {
        if (roomData == null || roomData.Count == 0) return;
#if UNITY_EDITOR
        foreach (RoomInfo room in roomData.Values)
        {
            if (room.roomRef != null)
            {
                Vector3 roomCenter = room.roomRef.transform.position;
                Gizmos.color = Color.Lerp(Color.blue, Color.red, room.generalCuriosity);
                Gizmos.DrawWireSphere(roomCenter, 0.5f);
                Handles.Label(roomCenter + Vector3.up * 2.0f, $"{room.roomName}\n{room.generalCuriosity:F2}");
            }
        }
#endif
    }
}
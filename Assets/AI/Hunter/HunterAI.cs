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
    #region --- CONFIGURATION & SETTINGS ---

    [Header("Decision Scoring")]
    [Tooltip("Multiplier for points in the same room as the Hunter. Keeps him focused.")]
    [Range(1.0f, 2.0f)]
    public float sameRoomMultiplier = 1.1f;

    [Tooltip("How much Distance reduces the score. Higher = Lazier Hunter.")]
    public float distancePenalty = 1.0f;

    [Header("Behavior Settings")]
    [Tooltip("After peeking a door, how long until he allows himself to peek another.")]
    public float peekSkillCooldown = 15.0f;

    [Header("Head Dynamics")]
    [Tooltip("Time between selecting new random gaze points.")]
    public float idleLookInterval = 2.0f;
    [Tooltip("Speed of head rotation when performing idle scan.")]
    public float idleHeadTurnSpeed = 2.0f;

    [Header("Movement Dynamics")]
    [Tooltip("Speed when peeking into a room.")]
    public float creepSpeed = 0.5f;
    [Tooltip("Distance to drift into the room while peeking.")]
    public float creepDistance = 1.5f;

    [Header("Probability Settings")]
    [HideInInspector] public float baseUncertainty = 0.2f;
    [SerializeField] private float probabilityUpdateInterval = 1f;

    [Header("Chase Settings")]
    [SerializeField] public float chaseInvestigationTime = 7.0f;

    [Header("Director Interaction")]
    [SerializeField] private float directorCommandPathCostThreshold = 40f;

    [Header("Debug")]
    [SerializeField] public string currentBTState = "Initializing";

    #endregion

    #region --- STATE & REFERENCES ---

    // Navigation & Targets
    public Transform currentInterestTarget = null;
    public Vector3 currentNavDestination = Vector3.zero;
    public Transform targetPos; // Chase target

    // State Tracking
    [HideInInspector] public float timeSinceLastSeen = 999.0f;
    [HideInInspector] public bool isChasingPlayer = false;
    private float nextPeekTime = 0f;
    private RoomInfo currentRoomInfo = null;
    private RoomInfo previousRoomInfo = null;
    private WaitForSeconds probabilityWait;

    // Memory
    [HideInInspector] public Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();
    [HideInInspector] public Dictionary<string, RoomInfo> roomData = new Dictionary<string, RoomInfo>();
    private Room[] rooms;

    // Components
    private NavMeshAgent agent;
    private Node rootNode;
    private HunterBehaviorNodes btContext;

    #endregion

    #region --- INITIALIZATION & LOOP ---

    void Start()
    {
        InitializeComponents();
        BuildMemoryMap();
        StartCoroutine(UpdateCuriosityRoutine());
        rootNode = SetupBehaviorTree();
        SubscribeEvents();
    }

    void Update()
    {
        if (rootNode != null) rootNode.Evaluate();

        // Timer Logic
        if (timeSinceLastSeen > 0.0f) timeSinceLastSeen += Time.deltaTime;

        if (isChasingPlayer && timeSinceLastSeen >= chaseInvestigationTime)
        {
            isChasingPlayer = false;
            timeSinceLastSeen = 999.0f;
        }
    }

    private void InitializeComponents()
    {
        agent = GetComponent<NavMeshAgent>();
        if (targetPos == null) targetPos = new GameObject("PlayerChaseTarget_Dynamic").transform;
        probabilityWait = new WaitForSeconds(probabilityUpdateInterval);
    }
    private void BuildMemoryMap()
    {
        rooms = FindObjectsOfType<Room>();
        PatrolPoints[] allPointsInScene = FindObjectsOfType<PatrolPoints>();

        Debug.Log($"[HunterAI] Found {rooms.Length} Rooms and {allPointsInScene.Length} Points.");

        // 1. Init Rooms
        foreach (Room room in rooms)
        {
            int exits = 1;
            RoomInfo newRoomInfo = new RoomInfo(room, exits);
            if (!roomData.ContainsKey(newRoomInfo.roomName))
                roomData.Add(newRoomInfo.roomName, newRoomInfo);
        }

        // 2. Sort Points
        foreach (PatrolPoints point in allPointsInScene)
        {
            Room ownerRoom = point.manualRoomOwner != null ? point.manualRoomOwner : point.GetComponentInParent<Room>();

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
                Debug.LogWarning($"[HunterAI] Point '{point.name}' has no Room!");
            }
        }

        // 3. Link Doors
        foreach (var kvp in patrolPointData)
        {
            PatrolPoints pointScript = kvp.Key.GetComponent<PatrolPoints>();
            HunterPatrolMemory memory = kvp.Value;

            if (pointScript.pointType == PointType.Doorway && pointScript.linkedRoom != null)
            {
                if (roomData.TryGetValue(pointScript.linkedRoom.name, out RoomInfo targetRoomInfo))
                    memory.linkedRoomInfo = targetRoomInfo;
            }
        }
    }

    #endregion

    #region --- CORE AI BRAIN (SCORING) ---

    public Transform GetBestNextPoint(Vector3 currentPos, List<Transform> ignorePoints = null)
    {
        Transform bestCandidate = null;
        float bestScore = float.NegativeInfinity;

        // Context Setup
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

            // Global Cooldown Filter
            if (memory.pointType == PointType.Doorway && Time.time < nextPeekTime) continue;

            // Backtrack Filter
            else if (memory.pointType == PointType.Doorway && memory.linkedRoomInfo == previousRoomInfo) continue;

            // Path Validation
            float trueWalkingDistance = float.PositiveInfinity;
            if (!IsPathValid(point.position, out trueWalkingDistance)) continue;

            // --- SCORING ---
            float score = 0f;

            // 1. Base Heat
            if (memory.pointType == PointType.Doorway && memory.linkedRoomInfo != null)
                score = memory.linkedRoomInfo.generalCuriosity * 100f;
            else
                score = memory.playerProbability * 100f;

            // 2. Momentum Bonus
            if (memory.parentRoom != null)
            {
                if (memory.parentRoom == primaryContext || memory.parentRoom == secondaryContext)
                    score *= sameRoomMultiplier;
            }

            // 3. Distance Penalty
            score -= (trueWalkingDistance * distancePenalty);

            // 4. Priority Override
            if (memory.IsWorthyOfInvestigation) score += 50f;

            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = point;
            }
        }

        return bestCandidate;
    }

    #endregion

    #region --- NAVIGATION HELPERS ---

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

    // FIX: Corrected syntax for out parameter assignment
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

    public float CalculatePathCost(NavMeshPath path)
    {
        float cost = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            cost += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }
        return cost;
    }

    public List<HunterPatrolMemory> GetLocalHotPoints(Vector3 center, float radius)
    {
        return patrolPointData.Values
            .Where(p => p.playerProbability > baseUncertainty && Vector3.Distance(center, p.patrolpointTransform.position) <= radius)
            .OrderByDescending(p => p.playerProbability)
            .ToList();
    }

    #endregion

    #region --- STATE MANAGEMENT & UTILITY ---

    public void RecordPatrolVisit(Transform point)
    {
        if (patrolPointData.TryGetValue(point, out HunterPatrolMemory memory))
        {
            // Room History
            if (memory.parentRoom != null && memory.parentRoom != currentRoomInfo)
            {
                previousRoomInfo = currentRoomInfo;
                currentRoomInfo = memory.parentRoom;
            }

            // Cooling
            memory.lastPatrolTime = Time.time;
            memory.playerProbability = baseUncertainty;
            memory.hasDirectorTip = false;
            memory.hasHeardNoise = false;
            if (memory.parentRoom != null) memory.parentRoom.UpdateGeneralCuriosity();

            // Behavior Trigger
            if (memory.pointType == PointType.Doorway)
            {
                nextPeekTime = Time.time + peekSkillCooldown;
            }
        }
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
                    memory.playerProbability = Mathf.Min(0.5f, memory.playerProbability + 0.02f);
            }
            foreach (RoomInfo room in roomData.Values)
                room.UpdateGeneralCuriosity();
        }
    }

    private Node SetupBehaviorTree()
    {
        btContext = new HunterBehaviorNodes(this, agent);

        // Nodes
        var isPlayerSeen = new HunterBehaviorNodes.IsPlayerSeen(btContext);
        var chasePlayer = new HunterBehaviorNodes.ChasePlayer(btContext);
        var acquireTarget = new HunterBehaviorNodes.AcquirePatrolTarget(btContext);
        var movePatrol = new HunterBehaviorNodes.MoveToPatrolPoint(btContext);

        // Action Nodes
        var isDoorway = new HunterBehaviorNodes.IsPatrolPointType(btContext, PointType.Doorway);
        var peekAction = new HunterBehaviorNodes.PerformDoorwayPeek(btContext);
        var standardAction = new HunterBehaviorNodes.PerformStandardAction(btContext);

        // Branches
        var chaseBranch = new Sequence("CHASE LOGIC", "High Priority: If player is visible, chase.",
            new List<Node> { isPlayerSeen, chasePlayer });

        var actionSelector = new Selector("CONTEXT ACTION", "Decides behavior based on point type.",
            new List<Node>
        {
            new Sequence("DOOR BEHAVIOR", "Peek into the room.", new List<Node> { isDoorway, peekAction }),
            standardAction
        });

        var patrolBranch = new Sequence("PATROL LOOP", "Find target, move, perform action.",
            new List<Node> { acquireTarget, movePatrol, actionSelector });

        return new Selector("ROOT AI", "Main Brain.", new List<Node> { chaseBranch, patrolBranch });
    }

    private void SubscribeEvents()
    {
        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove += OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;
        Actions.HunterSawPatrolPoint += OnPatrolPointSeen;
    }

    #endregion

    #region --- EVENT HANDLERS ---

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
    #endregion
}
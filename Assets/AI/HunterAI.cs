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

    // Discrete memory tags
    public bool hasHeardNoise = false;
    public bool hasDirectorTip = false;

    public bool IsWorthyOfInvestigation => hasHeardNoise || hasDirectorTip || playerProbability > 0.5f;

    public float calculatedPriorityScore = 0f;

    // Link to the parent room (The room this point physically sits inside)
    [System.NonSerialized] public RoomInfo parentRoom;

    // NEW: Link to the target room (The room a Doorway looks INTO)
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

    // --- Navigation & Targets ---
    public Transform currentPatrolTarget = null;
    public Transform targetPos;

    private NavMeshAgent agent;

    // --- Memory Data ---
    private Room[] rooms;

    [HideInInspector]
    public Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();

    [HideInInspector]
    public Dictionary<string, RoomInfo> roomData = new Dictionary<string, RoomInfo>();

    // --- SETTINGS: Scoring & Brain ---
    [Header("Decision Scoring")]
    [Tooltip("Multiplier for points in the same room as the Hunter. Keeps him focused.")]
    [Range(1.0f, 2.0f)]
    public float sameRoomMultiplier = 1.2f;

    [Tooltip("Multiplier for Doorway points. Encourages peeking before entering.")]
    [Range(1.0f, 2.0f)]
    public float doorwayMultiplier = 1.3f;

    [Tooltip("How much Distance reduces the score. Higher = Lazier Hunter.")]
    public float distancePenalty = 1.0f;

    [Header("Probability Settings")]
    [HideInInspector] public float baseUncertainty = 0.2f;
    [SerializeField] private float probabilityUpdateInterval = 1f;
    [SerializeField] private float wanderRange = 5f;
    private WaitForSeconds probabilityWait;

    // --- Chase Settings ---
    [Header("Chase Settings")]
    [SerializeField] public float chaseInvestigationTime = 7.0f;
    [HideInInspector] public float timeSinceLastSeen = 999.0f;
    [HideInInspector] public bool isChasingPlayer = false;

    // --- Director Settings ---
    [Header("Director Interaction")]
    [SerializeField] private float directorCommandPathCostThreshold = 40f;

    // --- Debug ---
    [Header("BT Debug")]
    [SerializeField] public string currentBTState = "Initializing";

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rooms = FindObjectsOfType<Room>();

        Debug.Log($"[HunterAI.Start] Found {rooms.Length} Room objects.");

        probabilityWait = new WaitForSeconds(probabilityUpdateInterval);

        // --- BUILD MEMORY ---
        foreach (Room room in rooms)
        {
            int exits = 1;
            RoomInfo newRoomInfo = new RoomInfo(room, exits);

            if (!roomData.ContainsKey(newRoomInfo.roomName))
            {
                roomData.Add(newRoomInfo.roomName, newRoomInfo);
            }

            // Loop through points in the room
            foreach (PatrolPoints point in room.patrolPoint)
            {
                if (!patrolPointData.ContainsKey(point.transform))
                {
                    HunterPatrolMemory newMemory = new HunterPatrolMemory
                    {
                        patrolpointTransform = point.transform,
                        pointType = point.pointType,
                        playerProbability = 0.5f,
                        lastPatrolTime = Time.time,
                        parentRoom = newRoomInfo
                    };

                    // --- NEW: Direct Injection for PatrolPoints.cs ---
                    point.runtimeMemory = newMemory;

                    patrolPointData.Add(point.transform, newMemory);
                    newRoomInfo.patrolPoints.Add(newMemory);
                }
            }
        }

        // --- SECOND PASS: LINK DOORS ---
        // We do this after all RoomInfos are created so we can find them by name
        foreach (Room room in rooms)
        {
            foreach (PatrolPoints point in room.patrolPoint)
            {
                if (point.pointType == PointType.Doorway && point.linkedRoom != null)
                {
                    if (roomData.TryGetValue(point.linkedRoom.name, out RoomInfo targetRoomInfo))
                    {
                        // Link the memory!
                        patrolPointData[point.transform].linkedRoomInfo = targetRoomInfo;
                    }
                }
            }
        }

        if (targetPos == null)
        {
            targetPos = new GameObject("PlayerChaseTarget_Dynamic").transform;
        }

        StartCoroutine(UpdateCuriosityRoutine());
        rootNode = SetupBehaviorTree();

        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove += OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;
        Actions.HunterSawPatrolPoint += OnPatrolPointSeen;
    }

    void Update()
    {
        if (rootNode != null)
        {
            rootNode.Evaluate();
        }

        if (timeSinceLastSeen > 0.0f)
        {
            timeSinceLastSeen += Time.deltaTime;
        }

        if (isChasingPlayer && timeSinceLastSeen >= chaseInvestigationTime)
        {
            isChasingPlayer = false;
            timeSinceLastSeen = 999.0f;
        }
    }

    // =================================================================================
    // --- THE SMART BRAIN: Get Best Next Point ---
    // =================================================================================
    public Transform GetBestNextPoint(Vector3 currentPos, List<Transform> ignorePoints = null)
    {
        Transform bestCandidate = null;
        float bestScore = float.NegativeInfinity;

        NavMeshPath path = new NavMeshPath();

        // MOMENTUM CONTEXT: What room is the Hunter currently "In"?
        RoomInfo currentRoomContext = null;
        if (currentPatrolTarget != null && patrolPointData.TryGetValue(currentPatrolTarget, out HunterPatrolMemory currMem))
        {
            currentRoomContext = currMem.parentRoom;
        }

        foreach (var pair in patrolPointData)
        {
            Transform point = pair.Key;
            HunterPatrolMemory memory = pair.Value;

            if (memory.playerProbability <= baseUncertainty) continue;
            if (ignorePoints != null && ignorePoints.Contains(point)) continue;

            // Optimization: Rough Distance Check
            if (Vector3.Distance(currentPos, point.position) > 25f) continue;

            // Optimization: Path Calculation
            float trueWalkingDistance = float.PositiveInfinity;
            if (NavMesh.CalculatePath(currentPos, point.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                trueWalkingDistance = CalculatePathCost(path);
            }
            else
            {
                continue;
            }

            // --- SCORING FORMULA ---

            // A. Base Heat
            float score = memory.playerProbability * 100f;

            // B. Type Bonus (Smart Door Logic)
            if (memory.pointType == PointType.Doorway)
            {
                // If this door looks INTO the room we are already in, it's useless.
                // (e.g., Kitchen Door looking into Kitchen, while we are standing in Kitchen)
                if (currentRoomContext != null && memory.linkedRoomInfo == currentRoomContext)
                {
                    // No Bonus. In fact, maybe penalty? For now, just standard heat.
                }
                else
                {
                    // It looks into a NEW room (or out to the Hallway), so it's high value.
                    score *= doorwayMultiplier;
                }
            }

            // C. Momentum Bonus (Incentivize Same Room)
            if (currentRoomContext != null && memory.parentRoom == currentRoomContext)
            {
                score *= sameRoomMultiplier;
            }

            // D. Distance Penalty
            score -= (trueWalkingDistance * distancePenalty);

            // E. Priority Override
            if (memory.IsWorthyOfInvestigation) score += 50f;

            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = point;
            }
        }

        return bestCandidate;
    }

    // ========================================================
    // --- BEHAVIOR TREE SETUP ---
    // ========================================================
    private Node SetupBehaviorTree()
    {
        btContext = new HunterBehaviorNodes(this, agent);

        var isPlayerSeen = new HunterBehaviorNodes.IsPlayerSeen(btContext);
        var chasePlayer = new HunterBehaviorNodes.ChasePlayer(btContext);
        var acquireTarget = new HunterBehaviorNodes.AcquirePatrolTarget(btContext);
        var movePatrol = new HunterBehaviorNodes.MoveToPatrolPoint(btContext);

        var chaseBranch = new Sequence(new List<Node> { isPlayerSeen, chasePlayer });
        var patrolBranch = new Sequence(new List<Node> { acquireTarget, movePatrol });

        var root = new Selector(new List<Node> { chaseBranch, patrolBranch });
        return root;
    }

    // =====================================================================
    // --- UTILITY & HELPERS ---
    // =====================================================================
    public void RecordPatrolVisit(Transform point)
    {
        if (patrolPointData.TryGetValue(point, out HunterPatrolMemory memory))
        {
            memory.lastPatrolTime = Time.time;
            memory.playerProbability = baseUncertainty;
            memory.hasDirectorTip = false;
            memory.hasHeardNoise = false;
            if (memory.parentRoom != null) memory.parentRoom.UpdateGeneralCuriosity();
        }
    }

    public List<HunterPatrolMemory> GetLocalHotPoints(Vector3 center, float radius)
    {
        return patrolPointData.Values
            .Where(p => p.playerProbability > baseUncertainty && Vector3.Distance(center, p.patrolpointTransform.position) <= radius)
            .OrderByDescending(p => p.playerProbability)
            .ToList();
    }

    public Vector3 GetRandomWanderPoint(Vector3 center, float range)
    {
        Vector3 randomPoint = center + UnityEngine.Random.insideUnitSphere * range;
        NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(randomPoint, out hit, range, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return Vector3.zero;
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

    // ===========================================
    // --- EVENT HANDLERS ---
    // ===========================================

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
}
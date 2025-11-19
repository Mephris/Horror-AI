using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
//Behavior Tree Namespace
using static Node;

//Linq queries
using System.Linq;

// For Editor Gizmo drawing
#if UNITY_EDITOR
using UnityEditor;
#endif


[System.Serializable]
public class HunterPatrolMemory
{
    public Transform patrolpointTransform;
    // Tracks the last time the hunter completed an investigation here
    public float lastPatrolTime = 0f;

    // THE CORE PROBABILITY SCORE (0.0=Clear to 1.0=Likely Here)
    public float playerProbability = 0f;

    // Discrete memory tags for specific events
    public bool hasHeardNoise = false;
    public bool hasSeenDisturbance = false;
    public bool hasDirectorTip = false;

    // Quick Check for BT to decide if this point is a high priority detour
    public bool IsWorthyOfInvestigation => hasHeardNoise || hasSeenDisturbance || hasDirectorTip || playerProbability > 0.5f;

    // The final value the BT will use to compare points.
    public float calculatedPriorityScore = 0f;

    // Link to the parent room for hierarchical planning
    [System.NonSerialized] public RoomInfo parentRoom;
}


public class HunterAI : MonoBehaviour
{
    // --- Behavior Tree & Planner Fields ---
    private Node rootNode;
    private HunterBehaviorNodes btContext;

    // NEW: The Planner's active "line of command."
    public HunterGoal activeGoal;

    // --- Navigation & Targets (Made public for HunterBehaviorNodes) ---
    public Transform currentPatrolTarget = null;
    public Transform targetPos; // Last known or commanded position

    private NavMeshAgent agent;

    // --- PatrolPoint & Room Data (MODIFIED) ---
    private Room[] rooms; // This is your 'Room.cs' component

    // MODIFIED: This is now the "Master List" of all patrol point memory.
    // It provides fast, "flat" lookups by Transform (for your "Glance Perk").
    [HideInInspector]
    public Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();

    // NEW: This is the "Level 1" hierarchical memory for the Planner.
    // It organizes the memory by room.
    [HideInInspector]
    public Dictionary<string, RoomInfo> roomData = new Dictionary<string, RoomInfo>();


    // --- Decay & Wander Settings ---
    [Header("Probability Settings")]
    [Tooltip("The minimum probability a point can be checked to (baseline uncertainty).")]
    [HideInInspector] public float baseUncertainty = 0.2f;
    [SerializeField] private float probabilityUpdateInterval = 1f;
    [SerializeField] private float wanderRange = 5f; // Used by GetRandomWanderPoint
    private WaitForSeconds probabilityWait;



    // --- Investigation Settings ---

    [Header("Investigation Duration")]
    [Tooltip("Base time (seconds) Hunter spends investigating a patrol point.")]
    [SerializeField] private float baseInvestigationTime = 5f;

    [Tooltip("Maximum multiplier applied to base time based on patrol point probability (e.g., probability of 1.0 gets baseTime * maxMultiplier).")]
    [SerializeField] private float maxProbabilityMultiplier = 2.0f; // A point with probability 1.0 would have a duration of 5s * 2.0 = 10s.


    // --- Director Command Settings ---
    [Header("Director Command Settings")]
    [Tooltip("The maximum NavMesh path cost (distance) a patrol point can be from the command location to be affected.")]
    [SerializeField] private float directorCommandPathCostThreshold = 40f; // New threshold

    // --- Chase Settings ---
    [Header("Chase Settings")]
    [Tooltip("Time (in seconds) the Hunter continues to investigate the last known location after losing sight.")]
    [SerializeField] public float chaseInvestigationTime = 7.0f;


    [HideInInspector] public float timeSinceLastSeen = 999.0f;
    [HideInInspector] public bool isChasingPlayer = false; // Flag for the BT

    // --- BT Debugging ---
    [Header("BT Debug")]
    [SerializeField] public string currentBTState = "Initializing";

    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rooms = FindObjectsOfType<Room>(); // Make sure your 'Room.cs' script exists

        // --- ADD THIS LINE ---
        Debug.Log($"[HunterAI.Start] Found {rooms.Length} Room objects in the scene.");
        // --- END ADD ---

        // 1. Initialize decay timer
        probabilityWait = new WaitForSeconds(probabilityUpdateInterval);

        // 2. Initialize memory for all patrol points in the scene
        // We now build both the flat (patrolPointData) and hierarchical (roomData) dictionaries.

        // 2a. Loop through all 'Room' GameObjects found in the scene
        foreach (Room room in rooms)
        {
            // 2b. Create the new "Level 1" RoomInfo data container
            // (Assuming your 'Room.cs' has an 'exitCount' property,
            // otherwise, you'll need to add it or set a default)
            int exits = 1; // Placeholder: You should get this from your 'Room.cs'
                           // if (room.exitCount != null) { exits = room.exitCount; } // Example

            // MODIFIED: Pass the 'room' (MonoBehaviour) to the constructor
            RoomInfo newRoomInfo = new RoomInfo(room, exits);
            if (!roomData.ContainsKey(newRoomInfo.roomName))
            {
                roomData.Add(newRoomInfo.roomName, newRoomInfo);
            }

            // 2c. Loop through all patrol points *within* this room
            foreach (PatrolPoints point in room.patrolPoint) // Assuming 'patrolPoint' is the list in 'Room.cs'
            {
                if (!patrolPointData.ContainsKey(point.transform))
                {
                    // 4. Create the "Level 2" memory object
                    HunterPatrolMemory newMemory = new HunterPatrolMemory
                    {
                        patrolpointTransform = point.transform,
                        playerProbability = 0.5f, // Start with some uncertainty
                        lastPatrolTime = Time.time,
                        parentRoom = newRoomInfo // Link it to its parent room
                    };

                    // 5. Add the new memory to *both* lists:
                    // A) The master "flat list" for fast lookups
                    patrolPointData.Add(point.transform, newMemory);

                    // B) The room's "hierarchical list" for planning
                    newRoomInfo.patrolPoints.Add(newMemory);
                }
            }
        }


        // Ensure targetPos is initialized
        if (targetPos == null)
        {
            // Instantiating a new GameObject to hold the dynamic position.
            targetPos = new GameObject("PlayerChaseTarget_Dynamic").transform;
        }

        // 3. Set the initial closest room (This logic might be obsolete now)
        // ClosestRoom(); // You can likely remove this FSM-era method

        // 4. Start the continuous probability decay
        StartCoroutine(UpdateCuriosityRoutine());

        // 5. Initialize the Behavior Tree Context and Root
        // btContext is set inside SetupBehaviorTree
        rootNode = SetupBehaviorTree();

        // 6. Subscription to Actions
        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove += OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;
        Actions.HunterSawPatrolPoint += OnPatrolPointSeen;
    }

    void Update()
    {
        // Execute the Behavior Tree every frame 
        if (rootNode != null)
        {
            rootNode.Evaluate();
            // TickBT(); // This is redundant if rootNode.Evaluate() is called
        }

        // 1. Increment the timer if the player is not actively seen (timeSinceLastSeen > 0).
        // The FieldOfView event sets timeSinceLastSeen = 0.0f when seen.
        if (timeSinceLastSeen > 0.0f)
        {
            timeSinceLastSeen += Time.deltaTime;
        }

        // 2. Clear the chasing state if the investigation timer expires.
        if (isChasingPlayer && timeSinceLastSeen >= chaseInvestigationTime)
        {
            isChasingPlayer = false; // <--- THIS is what makes IsPlayerSeen return FAILURE.
            timeSinceLastSeen = 999.0f; // Large value to indicate "not chasing"
        }
    }


    // ===========================================
    // --- EVENT HANDLERS (Simplified for BT) ---
    // ===========================================

    private void OnCommandToMove(Vector3 target)
    {
        ModifyMemoryNearLocation(target, 0.2f, false);
    }

    private void OnHighPriorityCommandToMove(Vector3 target)
    {
        ModifyMemoryNearLocation(target, 0.5f, true);
    }

    private void OnSeePlayer(bool isVisible, Vector3 lastPlayerLocation)
    {
        if (isVisible)
        {
            targetPos.position = lastPlayerLocation;
            timeSinceLastSeen = 0.0f;
            isChasingPlayer = true;
            agent.isStopped = false;
        }
        else // isVisible == false (Hunter lost sight)
        {
            if (isChasingPlayer && timeSinceLastSeen == 0.0f)
            {
                // Start the 7-second timer
                timeSinceLastSeen = Time.deltaTime;
            }
        }
    }

    private void OnPatrolPointSeen(Transform seenPointTransform)
    {
        // This lookup is still FAST because we kept the flat patrolPointData dictionary!
        if (patrolPointData.TryGetValue(seenPointTransform, out HunterPatrolMemory memory))
        {
            // --- 1. "GLANCE" PERK ---
            memory.lastPatrolTime = Time.time;

            // --- 2. "SCALED COLDNESS" ---
            if (memory.playerProbability > baseUncertainty)
            {
                float distance = Vector3.Distance(transform.position, seenPointTransform.position);
                float maxClearDistance = 25f;
                float minClearDistance = 5f;
                float maxClearAmount = 0.12f;
                float minClearAmount = 0.02f;

                float scale = Mathf.InverseLerp(maxClearDistance, minClearDistance, distance);
                float clearAmount = Mathf.Lerp(minClearAmount, maxClearAmount, scale);

                if (clearAmount > 0)
                {
                    memory.playerProbability = Mathf.Max(baseUncertainty, memory.playerProbability - clearAmount);
                    Debug.Log($"Hunter saw {seenPointTransform.name} (Dist: {distance:F0}m, Clear: {clearAmount:F2}). Prob REDUCED to: {memory.playerProbability:F2}");
                }
            }

            // --- 3. NEW: Update the parent room's heat ---
            // After cooling the point, we tell its parent room to recalculate its "generalCuriosity."
            if (memory.parentRoom != null)
            {
                memory.parentRoom.UpdateGeneralCuriosity();
            }

            // No need to write back to dictionary, 'memory' is a class (reference)
        }
    }

    // =====================================================================
    // --- WANDER LOGIC (Required by HunterBehaviorNodes.WanderLocally) ---
    // =====================================================================
    public Vector3 GetRandomWanderPoint(Vector3 center, float range)
    {
        Vector3 randomPoint = center + UnityEngine.Random.insideUnitSphere * range;
        NavMeshHit hit;

        if (UnityEngine.AI.NavMesh.SamplePosition(randomPoint, out hit, range, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }
        return Vector3.zero;
    }

    // --- Investigation Methods ---
    /* Is this obsolete with the BT?
   
    public float GetInvestigationDuration(Transform patrolPoint)
    {
        if (patrolPointData.TryGetValue(patrolPoint, out HunterPatrolMemory memory))
        {
            float probabilityFactor = memory.playerProbability * (maxProbabilityMultiplier - 1.0f);
            float finalDuration = baseInvestigationTime + (baseInvestigationTime * probabilityFactor);

            if (memory.hasDirectorTip && finalDuration < (baseInvestigationTime * maxProbabilityMultiplier))
            {
                return baseInvestigationTime * maxProbabilityMultiplier;
            }
            return finalDuration;
        }
        return baseInvestigationTime;
    }
    */

    public void RecordPatrolVisit(Transform point)
    {
        if (patrolPointData.TryGetValue(point, out HunterPatrolMemory memory))
        {
            memory.lastPatrolTime = Time.time;
            memory.playerProbability = baseUncertainty; // Set to minimum interest
            memory.hasDirectorTip = false;
            memory.hasHeardNoise = false;

            // Update parent room's curiosity
            if (memory.parentRoom != null)
            {
                memory.parentRoom.UpdateGeneralCuriosity();
            }
        }
    }

    // ==================================
    // --- CORE AI UTILITY FUNCTIONS ---
    // ==================================

    private IEnumerator UpdateCuriosityRoutine()
    {
        while (true)
        {
            yield return probabilityWait;

            // We iterate over the .Values collection, which gives us
            // a reference to each memory object.
            foreach (HunterPatrolMemory memory in patrolPointData.Values)
            {
                // --- CURIOSITY LOGIC ---
                float timeSinceLastVisit = Time.time - memory.lastPatrolTime;

                if (timeSinceLastVisit > 30f)
                {
                    // Because 'memory' is a class (a reference type),
                    // this line *directly modifies* the object in the dictionary.
                    memory.playerProbability = Mathf.Min(0.5f, memory.playerProbability + 0.02f);
                }

                // That's it! The line "patrolPointData[key] = memory;" is
                // deleted because we are modifying the object directly
                // and don't have (or need) a 'key' in this loop.
            }

            // After updating all points, update all rooms
            foreach (RoomInfo room in roomData.Values)
            {
                room.UpdateGeneralCuriosity();
            }
        }
    }

    // Helper function for the ObserveAndScan node
    public List<HunterPatrolMemory> GetHotPointsInRoom(RoomInfo room)
    {
        if (room == null || room.patrolPoints == null)
        {
            return new List<HunterPatrolMemory>(); // Return empty list
        }

        // Use LINQ to find all points in the room that are still "hot"
        // and sort them by distance so we scan the closest ones first.
        return room.patrolPoints
            .Where(p => p.playerProbability > baseUncertainty)
            .OrderBy(p => Vector3.Distance(transform.position, p.patrolpointTransform.position))
            .ToList();
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

    // ========================================================
    // --- BEHAVIOR TREE SETUP ---
    // ========================================================
    private Node SetupBehaviorTree()
    {
        // 1. Initialize the context that all nodes will share
        btContext = new HunterBehaviorNodes(this, agent);

        // =================================================================
        // --- 2. LEAF NODES (Referencing your existing node classes)
        // =================================================================

        // --- Chase Branch ---
        var isPlayerSeen = new HunterBehaviorNodes.IsPlayerSeen(btContext);
        var chasePlayer = new HunterBehaviorNodes.ChasePlayer(btContext);

        // --- Patrol/Goal Branch ---
        var moveToPatrolPoint = new HunterBehaviorNodes.MoveToPatrolPoint(btContext);
        var observeAndScan = new HunterBehaviorNodes.ObserveAndScan(btContext);

        // --- Planner (New Nodes) ---
        var hasActiveGoal = new HunterBehaviorNodes.HasActiveGoal(btContext);
        var findNewGoal = new HunterBehaviorNodes.Planner_FindNewGoal(btContext);


        // =================================================================
        // --- 3. BRANCHES (Building the tree from leaves up)
        // =================================================================

        // --- Priority 1: Chase Branch ---
        // If the player is seen, this sequence runs and locks the BT.
        var chaseBranch = new Sequence(new List<Node>
        {
            isPlayerSeen,
            chasePlayer
        });

        // --- Priority 2: Execute Goal Branch ---

        // This is the "core loop" of the AI's intelligent patrol.
        var executePatrolStep = new Sequence(new List<Node>
        {
            moveToPatrolPoint,
            observeAndScan
        });

        // This branch checks if we *have* a goal, and if so, executes it.
        var executeGoalBranch = new Sequence(new List<Node>
        {
            hasActiveGoal,
            executePatrolStep
        });

        // --- Priority 3: Get New Goal Branch ---
        var getNewGoalBranch = findNewGoal;


        // =================================================================
        // --- 4. ROOT (The final Selector)
        // =================================================================

        var root = new Selector(new List<Node>
        {
            chaseBranch,        // ALWAYS check for the player first.
            executeGoalBranch,  // THEN, try to execute our current plan.
            getNewGoalBranch    // OTHERWISE, get a new plan.
        });

        return root;
    }


    // ========================================================
    // --- GetBestPatrolRoute ---
    // ========================================================
    public List<Transform> GetBestPatrolRoute(int maxSteps = 4)
    {
        // 1. Get all points, ordered by probability (hottest first)
        var allPatrolPoints = patrolPointData.Values
            .OrderByDescending(p => p.playerProbability)
            .ToList();

        List<Transform> bestRoute = new List<Transform>();
        Transform lastPoint = transform; // Start from current Hunter position
        NavMeshPath path = new NavMeshPath();

        // Safety check: ensure we don't try to pathfind to the same point
        HashSet<Transform> visited = new HashSet<Transform>();
        float currentProbabilityThreshold = 0.5f; // Only start with points above a certain probability

        // 2. Build a route of up to maxSteps
        foreach (var memory in allPatrolPoints)
        {
            if (bestRoute.Count >= maxSteps)
                break;

            Transform nextPoint = memory.patrolpointTransform;

            // Only consider points that are hot enough or the first step
            if (memory.playerProbability < currentProbabilityThreshold && bestRoute.Count > 0)
            {
                continue;
            }

            // 3. Check for reachability from the last point in the path (or Hunter's start pos)
            if (!visited.Contains(nextPoint) &&
                UnityEngine.AI.NavMesh.CalculatePath(lastPoint.position, nextPoint.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                bestRoute.Add(nextPoint);
                visited.Add(nextPoint);
                lastPoint = nextPoint; // The next check is path from this newly added point
            }
        }

        return bestRoute;
    }

    // ========================================================
    // --- GetBestPatrolPoint ---
    // ========================================================

    public Transform GetBestPatrolPoint()
    {
        // If our goal is to search a room, only search that room.
        if (activeGoal != null && activeGoal.type == GoalType.SearchRoom && activeGoal.targetRoom != null)
        {
            return GetBestPatrolPointInRoom(activeGoal.targetRoom);
        }

        // If we have no goal, just find the best point on the whole map.
        Transform bestTarget = null;
        float highestPriorityScore = float.NegativeInfinity;

        Dictionary<Transform, float> debugScores = new Dictionary<Transform, float>();
        NavMeshPath path = new NavMeshPath();

        foreach (var pair in patrolPointData)
        {
            Transform pointTransform = pair.Key;
            HunterPatrolMemory memory = pair.Value;
            float score = 0f;

            // --- Simplified Score Calculation ---
            score += memory.playerProbability * 100f;
            if (memory.IsWorthyOfInvestigation)
            {
                score += 20f;
            }

            float pathCost;
            if (agent.CalculatePath(pointTransform.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                pathCost = CalculatePathCost(path);
                score -= pathCost * 0.05f;
            }
            else
            {
                score = float.NegativeInfinity; // Unreachable
            }

            debugScores[pointTransform] = score;

            if (score > highestPriorityScore)
            {
                highestPriorityScore = score;
                bestTarget = pointTransform;
            }
        }

        if (bestTarget != null)
        {
            HunterPatrolMemory bestMemory = patrolPointData[bestTarget];
            bestMemory.calculatedPriorityScore = highestPriorityScore;
        }

        return bestTarget;
    }

    // The filtered version of GetBestPatrolPoint
    public Transform GetBestPatrolPointInRoom(RoomInfo room)
    {
        Transform bestTarget = null;
        float highestPriorityScore = float.NegativeInfinity;
        NavMeshPath path = new NavMeshPath();

        // Loop *only* over the points in that room.
        foreach (HunterPatrolMemory memory in room.patrolPoints)
        {
            Transform pointTransform = memory.patrolpointTransform;
            float score = 0f;

            // --- Re-using your exact scoring logic ---
            score += memory.playerProbability * 100f;
            if (memory.IsWorthyOfInvestigation)
            {
                score += 20f;
            }

            float pathCost;
            if (agent.CalculatePath(pointTransform.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                pathCost = CalculatePathCost(path);
                score -= pathCost * 0.05f;
            }
            else
            {
                score = float.NegativeInfinity; // Unreachable
            }
            // --- End of scoring logic ---

            if (score > highestPriorityScore)
            {
                highestPriorityScore = score;
                bestTarget = pointTransform;
            }
        }

        if (bestTarget != null)
        {
            // We can update the memory struct directly since it's a class
            patrolPointData[bestTarget].calculatedPriorityScore = highestPriorityScore;
        }

        return bestTarget;
    }


    // ==============================================
    // --- DIRECTOR COMMAND MEMORY MODIFICATION ---
    // ==============================================
    private void ModifyMemoryNearLocation(Vector3 location, float probabilityIncrease, bool setDirectorTip)
    {
        List<Transform> pointsToUpdate = new List<Transform>();
        NavMeshPath path = new NavMeshPath();

        foreach (var pair in patrolPointData)
        {
            Transform pointTransform = pair.Key;
            if (NavMesh.CalculatePath(location, pointTransform.position, NavMesh.AllAreas, path))
            {
                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    float pathCost = CalculatePathCost(path);
                    if (pathCost <= directorCommandPathCostThreshold)
                    {
                        pointsToUpdate.Add(pointTransform);
                    }
                }
            }
        }

        // Second Pass: Apply modifications
        foreach (Transform pointTransform in pointsToUpdate)
        {
            HunterPatrolMemory memory = patrolPointData[pointTransform]; // Get reference
            memory.playerProbability = Mathf.Clamp01(memory.playerProbability + probabilityIncrease);
            if (setDirectorTip)
            {
                memory.hasDirectorTip = true;
            }

            // NEW: Update the room's curiosity
            if (memory.parentRoom != null)
            {
                memory.parentRoom.UpdateGeneralCuriosity();
            }
        }

        if (pointsToUpdate.Count > 0)
        {
            Debug.Log($"Director Command: Updated memory for **{pointsToUpdate.Count}** reachable patrol point(s). Prob Inc: {probabilityIncrease:F2}. Tip: {setDirectorTip}");
        }
        else
        {
            Debug.LogWarning("Director Command: No reachable patrol points found within the path cost threshold.");
        }
    }

    // -- GIZMO's FOR DEBUGGING --
    public float GetProbabilityScore(Transform patrolPoint)
    {
        if (patrolPointData.TryGetValue(patrolPoint, out HunterPatrolMemory memory))
        {
            return memory.playerProbability;
        }
        return 0f;
    }

    // --- GIZMO VISUALIZATION FOR ROOM HEAT ---
    // This function is called by Unity only in the Editor
    void OnDrawGizmos()
    {
        // Ensure we only run this logic if the data exists
        if (roomData == null || roomData.Count == 0)
        {
            return;
        }

        // We must wrap Editor-specific code to prevent build errors
#if UNITY_EDITOR
        foreach (RoomInfo room in roomData.Values)
        {
            if (room.roomRef != null) // Check if the room reference is valid
            {
                Vector3 roomCenter = room.roomRef.transform.position;

                // 1. Draw the "Heat" sphere
                // Color interpolates from Blue (0.0) to Red (1.0)
                Color gizmoColor = Color.Lerp(Color.blue, Color.red, room.generalCuriosity);
                gizmoColor.a = 0.3f; // Make it semi-transparent
                Gizmos.color = gizmoColor;
                Gizmos.DrawSphere(roomCenter, 0.5f); // Draw a solid sphere

                gizmoColor.a = 1.0f; // Make the outline solid
                Gizmos.color = gizmoColor;
                Gizmos.DrawWireSphere(roomCenter, 0.5f); // Draw the outline

                // 2. Draw the text label
                GUIStyle style = new GUIStyle();
                style.normal.textColor = Color.white;
                style.alignment = TextAnchor.MiddleCenter;

                string label = $"{room.roomName}\nHeat: {room.generalCuriosity:F2}";
                Handles.Label(roomCenter + Vector3.up * 2.0f, label, style);
            }
        }
#endif
    }
}
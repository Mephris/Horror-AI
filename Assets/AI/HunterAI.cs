using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
// Ensure you have this using statement for the Behavior Tree Node types
using static Node;


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
}


public class HunterAI : MonoBehaviour
{
    // --- Behavior Tree Fields ---
    private Node rootNode;
    private HunterBehaviorNodes btContext;

    // --- Navigation & Targets (Made public for HunterBehaviorNodes) ---
    public Transform currentPatrolTarget = null;
    public Transform targetPos; // Last known or commanded position

    private NavMeshAgent agent;

    // --- PatrolPoint & Room Data ---
    private Room[] rooms;
    private Room closestRoom;
    [HideInInspector] public Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();

    // --- Decay & Wander Settings ---
    [Header("Probability Settings")]
    [Tooltip("The minimum probability a point can be checked to (baseline uncertainty).")]
    [HideInInspector] public float baseUncertainty = 0.2f;
    [SerializeField] private float probabilityUpdateInterval = 1f;
    [SerializeField] private float wanderRange = 5f; // Used by GetRandomWanderPoint
    private WaitForSeconds probabilityWait;



    // --- Investigation Settings ---
    [Header("Investigation Timer")]
    [Tooltip("Flag set by the InvestigatePatrolPoint BT Node to track the current wait state.")]
    public bool isInvestigating = false;
    [HideInInspector] public float investigationTimeElapsed = 0f;
    [HideInInspector] public float investigationDuration = 0f; // The calculated duration for the current point.

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
        rooms = FindObjectsOfType<Room>();

        // 1. Initialize decay timer
        probabilityWait = new WaitForSeconds(probabilityUpdateInterval);

        // 2. Initialize memory for all patrol points in the scene
        foreach (Room room in rooms)
        {
            foreach (PatrolPoints point in room.patrolPoint)
            {
                if (!patrolPointData.ContainsKey(point.transform))
                {
                    patrolPointData.Add(point.transform, new HunterPatrolMemory
                    {
                        patrolpointTransform = point.transform,
                        playerProbability = 0.5f,
                        lastPatrolTime = Time.time
                    });
                }
            }
        }

        // Ensure targetPos is initialized
        if (targetPos == null)
        {
            // Instantiating a new GameObject to hold the dynamic position.
            targetPos = new GameObject("PlayerChaseTarget_Dynamic").transform;
        }

        // 3. Set the initial closest room
        ClosestRoom();

        // 4. Start the continuous probability decay
        StartCoroutine(UpdateCuriosityRoutine());

        // 5. Initialize the Behavior Tree Context and Root
        btContext = new HunterBehaviorNodes(this, agent);
        rootNode = SetupBehaviorTree();

        // 6. Subscription to Actions
        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove += OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;
        Actions.HunterSawPatrolPoint += OnPatrolPointSeen;

        // calculationInterval = Director.calculationInterval / 5.0f; // Leaving this commented out (old FSM logic)
    }

    void Update()
    {
        // Execute the Behavior Tree every frame 
        if (rootNode != null)
        {
            rootNode.Evaluate();
            TickBT();
        }

        // 1. Increment the timer if the player is not actively seen (timeSinceLastSeen > 0).
        // The FieldOfView event sets timeSinceLastSeen = 0.0f when seen.
        if (timeSinceLastSeen > 0.0f)
        {
            timeSinceLastSeen += Time.deltaTime;
        }

        // 2. Clear the chasing state if the investigation timer expires.
        // The chaseInvestigationTime is assumed to be defined in HunterAI.
        if (isChasingPlayer && timeSinceLastSeen >= chaseInvestigationTime)
        {
            // Debug.Log($"Investigation expired after {timeSinceLastSeen:F2} seconds.");
            isChasingPlayer = false; // <--- THIS is what makes IsPlayerSeen return FAILURE.

            // Safety: Reset the timer to avoid re-entering chase without seeing the player first.
            timeSinceLastSeen = 999.0f; // Large value to indicate "not chasing"
        }
    }


    // ===========================================
    // --- EVENT HANDLERS (Simplified for BT) ---
    // ===========================================

    // WARNING: Right now Command to move and HighPriorityCommandToMove both modify memory only up, they do not decrease it. 
    // CommandToMove is now a standard, non-critical 'noise' event
    private void OnCommandToMove(Vector3 target)
    {
        // Standard Command: Minor probability increase, no special 'Tip' flag
        ModifyMemoryNearLocation(target, 0.2f, false);
        // Note: We no longer set targetPos, the BT handles it based on memory
    }

    // HighPriorityCommandToMove is a strong, definitive clue
    private void OnHighPriorityCommandToMove(Vector3 target)
    {
        // High Priority Command: Significant probability increase, sets the special 'Tip' flag
        ModifyMemoryNearLocation(target, 0.5f, true);
        // Note: We no longer set targetPos
    }
    private void OnSeePlayer(bool isVisible, Vector3 lastPlayerLocation)
    {
        // When the Hunter sees the player, update the chase target
        if (isVisible)
        {
            targetPos.position = lastPlayerLocation;

            // Reset the timer and set the chasing flag
            timeSinceLastSeen = 0.0f;
            isChasingPlayer = true;

            agent.isStopped = false;
        }
        else // isVisible == false (Hunter lost sight)
        {
            if (isChasingPlayer && timeSinceLastSeen == 0.0f)
            {
                timeSinceLastSeen = Time.deltaTime;
            }
            // NOTE: targetPos MUST NOT be updated here, it stays at the last location
        }
    }
    private void OnPatrolPointSeen(Transform seenPointTransform)
    {
        if (patrolPointData.TryGetValue(seenPointTransform, out HunterPatrolMemory memory))
        {
            // --- 1. "GLANCE" PERK ---
            // Reset the "Curiosity" timer for this point. This is the "perk."
            memory.lastPatrolTime = Time.time;

            // --- 2. "SCALED COLDNESS" (YOUR IDEA) ---
            // Only cool down the point if it was "hot."
            if (memory.playerProbability > baseUncertainty)
            {
                // Calculate distance to scale the clear amount
                float distance = Vector3.Distance(transform.position, seenPointTransform.position);

                // Define our min/max ranges
                float maxClearDistance = 25f; // Max distance to have any effect
                float minClearDistance = 5f;  // Distance to have maximum effect
                float maxClearAmount = 0.12f; // <-- HALVED
                float minClearAmount = 0.02f; // <-- HALVED (approx)

                // Calculate the scaling factor (0.0 at max dist, 1.0 at min dist)
                float scale = Mathf.InverseLerp(maxClearDistance, minClearDistance, distance);

                // Calculate the final clear amount based on distance
                float clearAmount = Mathf.Lerp(minClearAmount, maxClearAmount, scale);

                if (clearAmount > 0)
                {
                    memory.playerProbability = Mathf.Max(baseUncertainty, memory.playerProbability - clearAmount);
                    Debug.Log($"Hunter saw {seenPointTransform.name} (Dist: {distance:F0}m, Clear: {clearAmount:F2}). Prob REDUCED to: {memory.playerProbability:F2}");
                }
            }

            // 3. Save the changes to the dictionary.
            patrolPointData[seenPointTransform] = memory;
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

    // ======================================================
    // --- PATROL LOGIC (Updated to use HasBeenVisited) ---
    // ======================================================
    // Keeps the logic that finds the closest room that isn't fully checked
    private void ClosestRoom()
    {
        Room roomNearby = null;
        float closestDistance = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            // Must update AllPointsChecked to use HasBeenVisited
            if (distance < closestDistance)
            {
                closestDistance = distance;
                roomNearby = room;
            }
        }
        closestRoom = roomNearby;
    }

    // Updated FindRoom_Command to use HasBeenVisited
    private Room FindRoom_Command(Vector3 target)
    {
        Room roomNearby = null;
        float closestDistance = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                roomNearby = room;
            }
        }
        return roomNearby;
    }

    // --- Investigation Methods ---

    // 1. Calculates the duration based on the point's probability score.
    public float GetInvestigationDuration(Transform patrolPoint)
    {
        // Retrieve memory, which should be guaranteed to exist at this point.
        if (patrolPointData.TryGetValue(patrolPoint, out HunterPatrolMemory memory))
        {
            // Probability scales the duration from base to (base * maxMultiplier)
            float probabilityFactor = memory.playerProbability * (maxProbabilityMultiplier - 1.0f);
            float finalDuration = baseInvestigationTime + (baseInvestigationTime * probabilityFactor);

            // Ensure Director Tips give at least the max duration, regardless of score
            if (memory.hasDirectorTip && finalDuration < (baseInvestigationTime * maxProbabilityMultiplier))
            {
                return baseInvestigationTime * maxProbabilityMultiplier;
            }

            return finalDuration;
        }

        return baseInvestigationTime;
    }

    // 2. Starts the investigation timer.
    public void StartInvestigation(Transform patrolPoint)
    {
        // 1. Set the calculated duration
        investigationDuration = GetInvestigationDuration(patrolPoint);

        // 2. Reset and start the timer
        investigationTimeElapsed = 0f;
        isInvestigating = true;
        agent.isStopped = true; // Stop movement while investigating

        // 3. Record the visit now (Replaces ArrivedAtPatrolPoint logic and sets lastPatrolTime)
        RecordPatrolVisit(patrolPoint);

        Debug.Log($"Starting Investigation at {patrolPoint.name}. Duration: {investigationDuration:F2}s.");
    }


    // 3. Updates the timer (called by the BT node)
    public Node.NodeState UpdateInvestigationTimer()
    {
        if (!isInvestigating)
        {
            return Node.NodeState.FAILURE;
        }

        investigationTimeElapsed += Time.deltaTime;

        if (investigationTimeElapsed >= investigationDuration)
        {
            // Investigation Complete
            isInvestigating = false;
            agent.isStopped = false;
            currentPatrolTarget = null; // Clear target for next Patrol selection
            return Node.NodeState.SUCCESS;
        }

        // Still waiting
        return Node.NodeState.RUNNING;
    }

    // 4. Record Patrol Visit & Memory Cleanup (Replaces HasBeenVisited = true)
    public void RecordPatrolVisit(Transform point)
    {
        if (patrolPointData.TryGetValue(point, out HunterPatrolMemory memory))
        {
            // Set the visit time to now
            memory.lastPatrolTime = Time.time;

            // Decay probability slightly upon successful investigation
            memory.playerProbability = baseUncertainty; // Set to minimum interest

            // Clear discrete high-priority tags upon arrival
            memory.hasDirectorTip = false;
            memory.hasHeardNoise = false;

            // Write the struct back to the dictionary
            patrolPointData[point] = memory;
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

            List<Transform> keys = new List<Transform>(patrolPointData.Keys);
            foreach (Transform key in keys)
            {
                HunterPatrolMemory memory = patrolPointData[key];

                // --- CURIOSITY LOGIC (THE FIX) ---
                // This point is "cold," so make it slowly heat up over time.
                float timeSinceLastVisit = Time.time - memory.lastPatrolTime;

                // After (e.g.) 30 seconds of not being seen, start increasing probability.
                if (timeSinceLastVisit > 30f)
                {
                    // Increase prob by a small amount, up to a max cap (e.g., 0.5)
                    // This makes it "interesting," but not as "hot" as a Director command.
                    memory.playerProbability = Mathf.Min(0.5f, memory.playerProbability + 0.05f);
                }

                patrolPointData[key] = memory;
            }
        }
    }

    // Helper method to calculate the cost of a NavMeshPath
    private float CalculatePathCost(NavMeshPath path)
    {
        float cost = 0f;

        // Sum up the cost of each corner in the path
        for (int i = 1; i < path.corners.Length; i++)
        {
            cost += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return cost;
    }

    // ========================================================
    // --- BEHAVIOR TREE SETUP (Placeholder implementation) ---
    // ========================================================
    private Node SetupBehaviorTree()
    {
        // ----------------------------------------------------------------------
        // Step 1: Define all the Leaf Nodes
        // ----------------------------------------------------------------------

        // CONDITIONS
        var isPlayerSeen = new HunterBehaviorNodes.IsPlayerSeen(btContext);
        var isAtDestination = new HunterBehaviorNodes.IsAtDestination(btContext);

        // TASKS
        var chasePlayer = new HunterBehaviorNodes.ChasePlayer(btContext);
        var movePatrol = new HunterBehaviorNodes.MoveToPatrolPoint(btContext);
        var wanderAround = new HunterBehaviorNodes.WanderLocally(btContext);

        // ----------------------------------------------------------------------
        // Step 2: Build the Main Branches
        // ----------------------------------------------------------------------

        // Priority 1: Chase -> IF Player Seen THEN Chase
        var chaseBranch = new Sequence(new List<Node> { isPlayerSeen, chasePlayer });

        // Priority 3: Patrol/Roam Loop
        // IF At Destination THEN Wander (Gives the Hunter the roaming behavior)
        var roamCheck = new Sequence(new List<Node> { isAtDestination, wanderAround });

        // Standard patrol: Try roaming first, otherwise move to next point.
        var patrolAndWander = new Selector(new List<Node> { roamCheck, movePatrol });

        // ----------------------------------------------------------------------
        // Step 3: Define the Root Selector (Highest Priority Check)
        // ----------------------------------------------------------------------

        // Priority Top-level: Chase > Patrol/Roam
        var root = new Selector(new List<Node> { chaseBranch, patrolAndWander });

        return root;
    }
    public Transform GetBestPatrolPoint()
    {
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

            // 1. "HEAT" (This score now includes both Director commands AND Curiosity)
            score += memory.playerProbability * 100f;

            // 2. "EVENTS": Flat bonus for high-priority memory tags
            if (memory.IsWorthyOfInvestigation)
            {
                score += 20f;
            }

            // 3. "EFFORT": Penalty for actual path cost.
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

            // --- End of Score Calculation ---

            debugScores[pointTransform] = score;

            if (score > highestPriorityScore)
            {
                highestPriorityScore = score;
                bestTarget = pointTransform;
            }
        }

        if (bestTarget == null)
        {
            Debug.LogWarning("No suitable patrol point found after checks.");
            return null;
        }

        HunterPatrolMemory bestMemory = patrolPointData[bestTarget];
        bestMemory.calculatedPriorityScore = highestPriorityScore;
        patrolPointData[bestTarget] = bestMemory;

        return bestTarget;
    }


    // ==============================================
    // --- DIRECTOR COMMAND MEMORY MODIFICATION ---
    // ==============================================
    // A helper method to modify memory near a given location for purpose of Director commands
    private void ModifyMemoryNearLocation(Vector3 location, float probabilityIncrease, bool setDirectorTip)
    {
        // FIX: Collection was modified error. 
        // Pass 1: Collect the keys (Transforms) that need modification first.
        List<Transform> pointsToUpdate = new List<Transform>();

        NavMeshPath path = new NavMeshPath();

        // 1. First Pass: Find all points that are nearby AND reachable via NavMesh
        foreach (var pair in patrolPointData) // Iterate over all points
        {
            Transform pointTransform = pair.Key;

            // Calculate the NavMesh Path from the command location to the patrol point
            if (NavMesh.CalculatePath(location, pointTransform.position, NavMesh.AllAreas, path))
            {
                // Check if a complete path exists and calculate its cost (distance)
                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    // This is the custom path cost calculation (helper below)
                    float pathCost = CalculatePathCost(path);

                    // Check if the path cost is below the defined threshold
                    if (pathCost <= directorCommandPathCostThreshold)
                    {
                        pointsToUpdate.Add(pointTransform);
                    }
                }
            }
        }

        // 2. Second Pass: Iterate over the collected keys and apply the memory modifications
        foreach (Transform pointTransform in pointsToUpdate)
        {
            // Get the current memory struct (value type, so we get a copy)
            HunterPatrolMemory memory = patrolPointData[pointTransform];

            // Increase probability (clamped between 0 and 1)
            memory.playerProbability = Mathf.Clamp01(memory.playerProbability + probabilityIncrease);

            // Set the Director Tip flag (only for High Priority Commands)
            if (setDirectorTip)
            {
                memory.hasDirectorTip = true;
            }

            // Update the dictionary with the modified struct
            patrolPointData[pointTransform] = memory;
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


    public void TickBT()
    {
        // Evaluate the root node
        Node.NodeState state = rootNode.Evaluate();

        // Debugging: Update the state string based on the result
        if (state == Node.NodeState.RUNNING)
        {
            // This is a simplistic check. A more detailed check requires checking the BT structure.
            // For now, let's keep it simple and update it in the nodes themselves.
        }
        else if (state == Node.NodeState.SUCCESS)
        {
            // The entire BT succeeded? Unlikely for a running AI.
        }

        // The most accurate way is to update this variable inside the highest priority successful node.
    }

    // -- GIZMO's FOR DEBUGGING --

    // Helper method for gizmos visualization
    public float GetProbabilityScore(Transform patrolPoint)
    {
        if (patrolPointData.TryGetValue(patrolPoint, out HunterPatrolMemory memory))
        {
            return memory.playerProbability;
        }
        return 0f; // Default to 0 (no priority) if the point is not tracked
    }


}
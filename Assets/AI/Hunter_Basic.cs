using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
// Ensure you have this using statement for the Behavior Tree Node types
using static Node;


[System.Serializable]
public class HunterPatrolMemory
{
    public Transform patrolpointTransform;
    public float lastPatrolTime = 0f;

    // THE CORE PROBABILITY SCORE (0.0=Clear to 1.0=Likely Here)
    public float playerProbability = 0f;

    // NEW DISCRETE MEMORY TAGS 
    public bool hasHeardNoise = false;
    public bool hasSeenDisturbance = false;
    public bool hasDirectorTip = false;

    // Quick Check for BT to decide if this point is a high priority detour
    public bool IsWorthyOfInvestigation => hasHeardNoise || hasSeenDisturbance || hasDirectorTip || playerProbability > 0.5f;

    // The final value the BT will use to compare points.
    public float calculatedPriorityScore = 0f;
}


public class Hunter_Basic : MonoBehaviour
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
    private Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();

    [Header("Patrol Memory Settings")]
    [Tooltip("Rate at which patrol point probability decays per second (e.g., 0.01 = 1% decrease per second).")]
    [SerializeField] private float memoryDecayRate = 0.01f;

    // --- Decay & Wander Settings ---
    [Header("Probability Settings")]
    [Tooltip("How much probability decays per second (e.g., 0.01 = 1% per second).")]
    [SerializeField] private float probabilityDecayRate = 0.01f;
    [Tooltip("The minimum probability a point can decay to (baseline uncertainty).")]
    [SerializeField] private float baseUncertainty = 0.2f;
    [SerializeField] private float decayUpdateInterval = 1f;
    [SerializeField] private float wanderRange = 5f; // Used by GetRandomWanderPoint
    private WaitForSeconds decayWait;

    // --- Investigation Duration Settings ---
    [Header("Investigation Duration")]
    [Tooltip("Base time (seconds) Hunter spends investigating a patrol point.")]
    [SerializeField] private float baseInvestigationTime = 5f;

    [Tooltip("Maximum multiplier applied to base time based on patrol point probability (e.g., probability of 1.0 gets baseTime * maxMultiplier).")]
    [SerializeField] private float maxProbabilityMultiplier = 2.0f;
    // A point with probability 1.0 would have a duration of 5s * 2.0 = 10s.

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
        decayWait = new WaitForSeconds(decayUpdateInterval);

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
                        playerProbability = baseUncertainty
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
        StartCoroutine(DecayProbabilitiesRoutine());

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
        // The chaseInvestigationTime is assumed to be defined in Hunter_Basic.
        if (isChasingPlayer && timeSinceLastSeen >= chaseInvestigationTime)
        {
            // Debug.Log($"Investigation expired after {timeSinceLastSeen:F2} seconds.");
            isChasingPlayer = false; // <--- THIS is what makes IsPlayerSeen return FAILURE.

            // Safety: Reset the timer to avoid re-entering chase without seeing the player first.
            timeSinceLastSeen = 999.0f; // Large value to indicate "not chasing"
        }

        // 3. Run Memory Decay (assuming this is called in Update or a Coroutine)
        DecayPatrolMemory();
    }




    // ============================================================
    // --- EVENT HANDLERS (Simplified for BT) ---
    // ============================================================

    //WARNING: Right now Command to move and HighPriorityCommandToMove both modify memory only up, they do not decrease it. 
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
        if (patrolPointData.ContainsKey(seenPointTransform))
        {
            HunterPatrolMemory memory = patrolPointData[seenPointTransform];

            // Logic: Seeing a Patrol Point means the area is "clear" for that moment.
            float clearAmount = 0.1f;
            memory.playerProbability = Mathf.Max(0f, memory.playerProbability - clearAmount);

            // Clear any memory tags that would be resolved by sight
            memory.hasSeenDisturbance = false;

            patrolPointData[seenPointTransform] = memory;

            Debug.Log($"Hunter saw {seenPointTransform.name}. Probability REDUCED to: {memory.playerProbability}");
        }
    }

    // --- WANDER LOGIC (Required by HunterBehaviorNodes.WanderLocally) ---
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


    // --- PATROL LOGIC (Updated to use HasBeenVisited) ---

    // Keeps the logic that finds the closest room that isn't fully checked
    private void ClosestRoom()
    {
        Room roomNearby = null;
        float closestDistance = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            // Must update AllPointsChecked to use HasBeenVisited
            if (distance < closestDistance && !AllPointsChecked(room))
            {
                closestDistance = distance;
                roomNearby = room;
            }
        }
        closestRoom = roomNearby;
    }

    // Updated AllPointsChecked to use HasBeenVisited
    private bool AllPointsChecked(Room room)
    {
        foreach (PatrolPoints point in room.patrolPoint)
        {
            if (!point.GetComponent<PatrolPoints>().HasBeenVisited) // <-- CHANGED
            {
                return false;
            }
        }
        return true;
    }

    // Arrival Handler for Patrol Points
    public void ArrivedAtPatrolPoint()
    {
        if (currentPatrolTarget != null)
        {
            // 1. **Mark the Point as Arrived/Visited (Memory Update)**
            PatrolPoints pointComponent = currentPatrolTarget.GetComponent<PatrolPoints>();
            if (pointComponent != null)
            {
                // We assume the PatrolPoints component has a public property/field called HasBeenVisited
                // The AllPointsChecked method already uses this property: point.GetComponent<PatrolPoints>().HasBeenVisited
                pointComponent.HasBeenVisited = true; // <--- ADDED LINE
            }

            // For now, let's just log it to ensure the logic fires:
            Debug.Log($"Hunter arrived at Patrol Point: {currentPatrolTarget.gameObject.name}. Clearing target.");

            // 2. **Clear the Current Target**
            // Clearing the target forces the next tick of MoveToPatrolPoint
            // to call GetBestPatrolPoint() and select a new destination.
            currentPatrolTarget = null;
        }
    }

    // Updated FindRoom_Command to use HasBeenVisited
    private Room FindRoom_Command(Vector3 target)
    {
        Room roomNearby = null;
        float closestDistance = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            if (distance < closestDistance && !AllPointsChecked(room))
            {
                closestDistance = distance;
                roomNearby = room;
            }
        }
        return roomNearby;
    }

    // This is good to keep for when a full sweep is required
    private void ResetRoomPatrolPoints(Room room)
    {
        foreach (var point in room.patrolPoint)
        {
            point.ResetCheckStatus();
        }
    }

    // Scales the investigation duration based on patrol point probability in the memory. 
    public float GetInvestigationDuration(Transform patrolPoint)
    {
        // 1. Get the memory for the patrol point.
        if (patrolPointData.TryGetValue(patrolPoint, out HunterPatrolMemory memory))
        {
            // 2. Calculate the scaled time.
            // Final Duration = Base Time + (Base Time * Probability * (Multiplier - 1))
            // Example: If prob=0, Duration = 5s. If prob=1.0, Duration = 5s + (5s * 1.0 * (2.0 - 1)) = 10s.
            float probabilityFactor = memory.playerProbability * (maxProbabilityMultiplier - 1.0f);
            float finalDuration = baseInvestigationTime + (baseInvestigationTime * probabilityFactor);

            // Ensure Director Tips give at least the max duration, regardless of probability score
            if (memory.hasDirectorTip && finalDuration < (baseInvestigationTime * maxProbabilityMultiplier))
            {
                return baseInvestigationTime * maxProbabilityMultiplier;
            }

            return finalDuration;
        }

        // Fallback: If for some reason the point isn't in the dictionary, return base time.
        return baseInvestigationTime;
    }

    // --- CORE AI UTILITY FUNCTIONS ---

    private IEnumerator DecayProbabilitiesRoutine()
    {
        while (true)
        {
            yield return decayWait;

            List<Transform> keys = new List<Transform>(patrolPointData.Keys);
            foreach (Transform key in keys)
            {
                HunterPatrolMemory memory = patrolPointData[key];

                if (memory.playerProbability > baseUncertainty)
                {
                    float decayAmount = probabilityDecayRate * decayUpdateInterval;
                    memory.playerProbability = Mathf.Max(baseUncertainty, memory.playerProbability - decayAmount);
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

    private void DecayPatrolMemory()
    {
        // Create a temporary list to hold the modified memory structs
        List<Transform> keysToUpdate = new List<Transform>(patrolPointData.Keys);

        foreach (Transform pointTransform in keysToUpdate)
        {
            HunterPatrolMemory memory = patrolPointData[pointTransform];

            // 1. Decay the player probability over time
            memory.playerProbability -= memoryDecayRate * Time.deltaTime;

            // 2. Clamp the probability to ensure it never goes below 0
            memory.playerProbability = Mathf.Max(0f, memory.playerProbability);

            // 3. Clear the Director Tip flag if probability is very low
            if (memory.playerProbability < 0.1f)
            {
                memory.hasDirectorTip = false;
            }

            // 4. Update the dictionary with the modified struct
            patrolPointData[pointTransform] = memory;
        }
    }


    // --- BEHAVIOR TREE SETUP (Placeholder implementation) ---

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
        // Start with a high negative priority so any valid point is better
        float highestPriorityScore = float.NegativeInfinity;

        // Find the closest room that isn't fully checked, or just use the current closest room.
        ClosestRoom();
        Room targetRoom = closestRoom;

        // Fallback: If no room is currently 'closest' or available, we can't move.
        if (targetRoom == null)
        {
            Debug.LogWarning("No available target room found for patrolling.");
            return null;
        }

        // Iterate through all patrol points in the target room
        foreach (PatrolPoints point in targetRoom.patrolPoint)
        {
            Transform pointTransform = point.transform;

            // Skip points that are already marked as visited recently (HasBeenVisited)
            if (point.HasBeenVisited)
                continue;

            // Get the memory for this point
            if (patrolPointData.TryGetValue(pointTransform, out HunterPatrolMemory memory))
            {
                // Calculation Strategy:
                // 1. Base Score: Probability of player presence (memory.playerProbability)
                // 2. Bonus: If the point has discrete memory tags (noise/tip)
                // 3. Penalty: Distance to the point (we prefer closer points)

                float distance = Vector3.Distance(transform.position, pointTransform.position);

                // Start the score with the highest weighted factor (usually probability)
                float score = memory.playerProbability * 10f; // Scale probability to matter more

                // Add a major bonus if it's worthy of investigation
                if (memory.IsWorthyOfInvestigation)
                {
                    score += 5f;
                }

                // Subtract distance from the score (closer is better). 
                // Normalize distance to avoid huge penalties (e.g., distance / 100)
                score -= distance * 0.1f;

                // Update the memory score for debugging/visualizing
                memory.calculatedPriorityScore = score;
                patrolPointData[pointTransform] = memory;

                if (score > highestPriorityScore)
                {
                    highestPriorityScore = score;
                    bestTarget = pointTransform;
                }
            }
        }

        // Final check: If the highest score is still negative infinity, it means all points were skipped or invalid.
        if (bestTarget == null && targetRoom != null)
        {
            // All points in this room are checked, so reset the room and try again in the next frame.
            Debug.Log($"All points in {targetRoom.name} checked. Resetting status.");
            ResetRoomPatrolPoints(targetRoom);
            // Return null for this frame so the BT task fails, but the next frame it will succeed.
            return null;
        }

        return bestTarget;
    }

    // --- DIRECTOR COMMAND MEMORY MODIFICATION ---
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


}
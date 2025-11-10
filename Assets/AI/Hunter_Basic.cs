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
    [SerializeField] public Transform currentPatrolTarget = null;
    [SerializeField] public Transform targetPos; // Last known or commanded position

    private NavMeshAgent agent;
    private Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();

    // --- Removed FSM variables: isMoving, isNotChasing, States enum, states, previousState ---

    // --- PatrolPoint & Room Data ---
    private Room[] rooms;
    private Room closestRoom;

    // --- Decay & Wander Settings ---
    [Header("Probability Settings")]
    [Tooltip("How much probability decays per second (e.g., 0.01 = 1% per second).")]
    [SerializeField] private float probabilityDecayRate = 0.01f;
    [Tooltip("The minimum probability a point can decay to (baseline uncertainty).")]
    [SerializeField] private float baseUncertainty = 0.2f;
    [SerializeField] private float decayUpdateInterval = 1f;
    [SerializeField] private float wanderRange = 5f; // Used by GetRandomWanderPoint

    private WaitForSeconds decayWait;


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
        }
    }

    // --- EVENT HANDLERS (Simplified for BT) ---

    private void OnCommandToMove(Vector3 target)
    {
        // This command sets a non-priority target. The BT will decide if it should chase or patrol instead.
        if (targetPos == null)
        {
            targetPos = new GameObject("CommandTarget").transform;
        }
        targetPos.position = target;
        // The BT's IsPlayerSeen condition should check targetPos, which now acts as a command target
    }

    private void OnHighPriorityCommandToMove(Vector3 target)
    {
        // High priority moves simply overwrite the target. The BT will react to this.
        if (targetPos == null)
        {
            targetPos = new GameObject("HPCommandTarget").transform;
        }
        targetPos.position = target;
    }

    private void OnSeePlayer(bool isVisible, Vector3 lastPlayerLocation)
    {
        // When the Hunter sees the player, update the chase target
        if (isVisible)
        {
            if (targetPos == null)
            {
                targetPos = new GameObject("PlayerChaseTarget").transform;
            }
            targetPos.position = lastPlayerLocation;
        }
        else if (targetPos != null && targetPos.name.Contains("PlayerChaseTarget"))
        {
            // Optional: If the player is lost, you might clear the target or set a temporary investigation point.
            // For now, let the chase sequence in the BT handle the loss of target.
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
        var chasePlayer = new PlaceholderTask("Chase Player");
        var movePatrol = new PlaceholderTask("Move to Patrol Point");
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

    // --- In Hunter_Basic.cs (Add this method) ---

    // --- NEW CORE BT LOGIC: GetBestPatrolPoint ---
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

    // --- PLACEHOLDER NODE DEFINITIONS (REQUIRED FOR COMPILATION) ---

    private class PlaceholderTask : Node
    {
        private string taskName;
        public PlaceholderTask(string name) { taskName = name; }
        public override NodeState Evaluate()
        {
            // return NodeState.SUCCESS; // Will be replaced by your real Task
            return NodeState.RUNNING; // Tasks should usually run until complete
        }
    }

    private class PlaceholderCondition : Node
    {
        private string conditionName;
        public PlaceholderCondition(string name) { conditionName = name; }
        public override NodeState Evaluate()
        {
            return NodeState.FAILURE; // Will be replaced by your real Condition
        }
    }
}
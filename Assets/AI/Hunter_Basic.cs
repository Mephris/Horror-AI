using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.AI;
using static Node; // Allows you to use NodeState directly (SUCCESS, FAILURE, RUNNING)


[System.Serializable]
public class HunterPatrolMemory
{
    public Transform patrolpointTransform;
    public float lastPatrolTime = 0f;

    // THE CORE PROBABILITY SCORE (0.0=Clear to 1.0=Likely Here)
    public float playerProbability = 0f;

    // NEW DISCRETE MEMORY TAGS (Set by events, cleared upon investigation)
    public bool hasHeardNoise = false;
    public bool hasSeenDisturbance = false;
    public bool hasDirectorTip = false;

    // Quick Check for BT to decide if this point is a high priority detour
    public bool IsWorthyOfInvestigation => hasHeardNoise || hasSeenDisturbance || hasDirectorTip || playerProbability > 0.5f;

    // The final value the BT will use to compare this point against all others.
    public float calculatedPriorityScore = 0f;
}

public class Hunter_Basic : MonoBehaviour
{
    // --- In Hunter_Basic.cs (inside the Hunter_Basic class) ---
    private HunterBehaviorNodes btContext;

    // --- Navigation & Hunter Tags ---
    private NavMeshAgent agent;

    private bool isMoving = false;
    private bool isNotChasing = true;

    private float calculationInterval;
    private float calculationElapsedTime = 0f;

    [SerializeField] private Transform currentPatrolTarget = null;
    [SerializeField] private Transform targetPos;

    private Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>(); // A dictionary to store all patrol point data

    [Header("Probability Settings")]
    [Tooltip("How much probability decays per second (e.g., 0.01 = 1% per second).")]
    [SerializeField] private float probabilityDecayRate = 0.01f;
    [Tooltip("The minimum probability a point can decay to (baseline uncertainty).")]
    [SerializeField] private float baseUncertainty = 0.2f;
    [SerializeField] private float decayUpdateInterval = 1f;
    private WaitForSeconds decayWait;

    //PatrolPoint Locations
    private Room[] rooms;
    private Room closestRoom;


    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rooms = FindObjectsOfType<Room>();

        // 1. Initialize decay timer
        decayWait = new WaitForSeconds(decayUpdateInterval);

        // 2. Initialize memory for all patrol points in the scene
        // This uses the Room array to ensure it initializes points from Room components
        foreach (Room room in rooms)
        {
            foreach (PatrolPoints point in room.patrolPoint)
            {
                if (!patrolPointData.ContainsKey(point.transform))
                {
                    patrolPointData.Add(point.transform, new HunterPatrolMemory
                    {
                        patrolpointTransform = point.transform,
                        playerProbability = baseUncertainty // Start with a baseline of uncertainty (0.2)
                    });
                }
            }
        }

        // 3. Set the initial closest room (kept your existing logic)
        ClosestRoom();

        // 4. Start the continuous probability decay
        StartCoroutine(DecayProbabilitiesRoutine());

        // 5. Initialize the Behavior Tree (Requires SetupBehaviorTree method)
        btContext = new HunterBehaviorNodes(this, agent);
        rootNode = SetupBehaviorTree();

        // 6. Subscription to Actions (Updated to include OnPatrolPointSeen)
        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove += OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;
        Actions.HunterSawPatrolPoint += OnPatrolPointSeen;

        // 7. Remaining initialization
        // The calculationInterval is a static variable from Director (assuming you handle its definition)
        // calculationInterval = Director.calculationInterval / 5.0f;
    }

    // ----------------------------------------------------
    // REQUIRED HELPER: Probability Decay Routine
    // ----------------------------------------------------
    private IEnumerator DecayProbabilitiesRoutine()
    {
        while (true)
        {
            yield return decayWait;

            // Use Keys to avoid modifying the dictionary while iterating
            List<Transform> keys = new List<Transform>(patrolPointData.Keys);
            foreach (Transform key in keys)
            {
                HunterPatrolMemory memory = patrolPointData[key];

                // Decay logic: Probability slowly returns down to the baseline uncertainty (0.2)
                if (memory.playerProbability > baseUncertainty)
                {
                    // Decay amount is calculated over the time interval
                    float decayAmount = probabilityDecayRate * decayUpdateInterval;
                    memory.playerProbability = Mathf.Max(baseUncertainty, memory.playerProbability - decayAmount);
                }

                patrolPointData[key] = memory; // Re-assign the struct to update the dictionary
            }
        }
    }


    // --- Updated SetupBehaviorTree() in Hunter_Basic.cs ---
    private Node SetupBehaviorTree()
    {
        // ----------------------------------------------------------------------
        // Step 1: Define all the Leaf Nodes using the BT Context
        // ----------------------------------------------------------------------

        // CONDITIONS
        var isPlayerSeen = new HunterBehaviorNodes.IsPlayerSeen(btContext);
        var isAtDestination = new HunterBehaviorNodes.IsAtDestination(btContext);

        // TASKS
        var chasePlayer = new PlaceholderTask("Chase Player"); // You'll write this next
        var movePatrol = new PlaceholderTask("Move to Patrol Point"); // You'll write this next
        var wanderAround = new HunterBehaviorNodes.WanderLocally(btContext);

        // ----------------------------------------------------------------------
        // Step 2: Build the Main Branches (Sequences and Selectors)
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

    // ----------------------------------------------------
    // REQUIRED HELPER: OnPatrolPointSeen Handler
    // ----------------------------------------------------
    private void OnPatrolPointSeen(Transform seenPointTransform)
    {
        if (patrolPointData.ContainsKey(seenPointTransform))
        {
            HunterPatrolMemory memory = patrolPointData[seenPointTransform];

            // DECREASE probability because the Hunter cleared the area with sight (e.g., by 0.1)
            float clearAmount = 0.1f;
            memory.playerProbability = Mathf.Max(0f, memory.playerProbability - clearAmount);

            // Also clear any memory tags that would be resolved by sight
            memory.hasSeenDisturbance = false;

            patrolPointData[seenPointTransform] = memory;

            Debug.Log($"Hunter saw {seenPointTransform.name}. Probability REDUCED to: {memory.playerProbability}");
        }
    }

    // Update is called once per frame
    void Update()
    {
        StateHandler();
    }


    //--------
    // STATES
    //--------
    /*
    private void StateHandler()
    {
        if (Time.time - calculationElapsedTime >= calculationInterval)
        {
            switch (states)
            {
                case States.Patrol:
                    agent.speed = 2.0f;
                    Patrol();

                    break;

                case States.SwitchRoom:
                    agent.speed = 3.0f;
                    if (!isMoving)
                    {
                        agent.SetDestination(ClosestRoom().transform.position);
                        isMoving = true;
                    }
                    if (agent.remainingDistance < 1.0f)
                    {
                        isMoving = false;
                        states = States.Patrol;
                    }
                    Debug.Log($"Hoonter Switched Room");
                    break;

                case States.Chase:

                    agent.speed = 3.5f;
                    break;

                case States.ExecuteOrder:

                    agent.speed = 3.0f;
                    isMoving = true;
                    previousState = states;
                    if (agent.remainingDistance <= 3.0f)
                    {
                        isMoving = false;
                        closestRoom = CurrentRoom();
                        states = States.Patrol;
                    }
                    break;

                case States.ExecuteHPOrder:

                    agent.speed = 4.5f;
                    isMoving = true;
                    previousState = states;
                    if (!isNotChasing)
                    {
                        Actions.HunterCanSeePlayer -= OnSeePlayer;
                    }
                    if (agent.remainingDistance <= 3.0f)
                    {
                        Actions.HunterCanSeePlayer += OnSeePlayer;
                        isMoving = false;
                        closestRoom = CurrentRoom();
                        states = States.Patrol;

                    }
                    break;
            }
            calculationElapsedTime = Time.time;
        }
    }
    */

    private void StateHandler()
    {
        if (Time.time - calculationElapsedTime >= calculationInterval)
        {
            switch (states)
            {
                case States.Patrol:
                    agent.speed = 2.0f;
                    Patrol();

                    break;

                case States.SwitchRoom:
                    agent.speed = 3.0f;
                    if (!isMoving)
                    {
                        // --- FIX: Ensure ClosestRoom() returns the *closest* room ---
                        // (Note: Your ClosestRoom() function returns the *second* closest, 
                        // but we'll stick to that for now as the core bug is elsewhere)
                        Room nextRoom = ClosestRoom();
                        if (nextRoom != null)
                        {
                            agent.SetDestination(nextRoom.transform.position);
                            isMoving = true;
                        }
                        else
                        {
                            // No valid room found, just go back to patrol
                            states = States.Patrol;
                        }
                    }

                    // --- ADDED pathPending check ---
                    if (!agent.pathPending && agent.remainingDistance < 1.0f)
                    {
                        isMoving = false;
                        states = States.Patrol;
                    }
                    Debug.Log($"Hoonter Switched Room");
                    break;

                case States.Chase:

                    agent.speed = 3.5f;
                    // Chase logic is handled by OnSeePlayer
                    break;

                case States.ExecuteOrder:
                    agent.speed = 3.0f;
                    isMoving = true;
                    previousState = states;

                    // --- THIS IS THE FIX ---
                    // Don't check distance until the path is calculated
                    if (!agent.pathPending)
                    {
                        if (agent.remainingDistance <= 3.0f)
                        {
                            isMoving = false;
                            closestRoom = CurrentRoom();
                            states = States.Patrol;
                        }
                        // Add a safety check for unreachable destinations
                        else if (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathStatus == NavMeshPathStatus.PathPartial)
                        {
                            isMoving = false;
                            states = States.Patrol; // Give up and go patrol
                        }
                    }
                    break;

                case States.ExecuteHPOrder:
                    agent.speed = 4.5f;
                    isMoving = true;
                    previousState = states;
                    if (!isNotChasing)
                    {
                        Actions.HunterCanSeePlayer -= OnSeePlayer;
                    }

                    // --- THIS IS THE FIX ---
                    // Don't check distance until the path is calculated
                    if (!agent.pathPending)
                    {
                        if (agent.remainingDistance <= 3.0f)
                        {
                            Actions.HunterCanSeePlayer += OnSeePlayer;
                            isMoving = false;
                            closestRoom = CurrentRoom();
                            states = States.Patrol;
                        }
                        // Add a safety check for unreachable destinations
                        else if (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathStatus == NavMeshPathStatus.PathPartial)
                        {
                            Actions.HunterCanSeePlayer += OnSeePlayer;
                            isMoving = false;
                            states = States.Patrol; // Give up and go patrol
                        }
                    }
                    break;
            }
            calculationElapsedTime = Time.time;
        }
    }
    private void SelectNextPatrolPoint()
    {
        HunterPatrolMemory bestPatrolMemory = null;
        float bestScore = -1f;

        // Iterate through all the patrol points in our memory
        foreach (var kvp in patrolPointData)
        {
            HunterPatrolMemory memory = kvp.Value;

            // --- Your decision-making logic goes here ---`
            // For example, prioritize points that haven't been patrolled in a long time.`
            float timeSincePatrolled = Time.time - memory.lastPatrolTime;

            // A simple score could be just the time since last patrol, or a combination with probability.`
            float currentScore = timeSincePatrolled + (memory.playerProbability * 100); // Probability has a higher weight

            if (currentScore > bestScore)
            {
                bestScore = currentScore;
                bestPatrolMemory = memory;
            }
        }

        // If we found a point to patrol`
        if (bestPatrolMemory != null)
        {
            currentPatrolTarget = bestPatrolMemory.patrolpointTransform;
            agent.SetDestination(currentPatrolTarget.position);
            isMoving = true;
        }
        // Handle the case where all points have been patrolled very recently (e.g., states = States.SwitchRoom)`
        else
        {
            // This is where you might implement your "SwitchRoom" logic if there are no good points left`
        }
    }

    private Room ClosestRoom()
    {
        Room currentRoom = null;
        float closestPathLength = Mathf.Infinity;
        Room closestRoomCandidate = null;
        float closestPathLengthCandidate = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            // Calculate the path from the current position to the room
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(transform.position, room.transform.position, NavMesh.AllAreas, path))
            {
                // Get the length of the path
                float pathLength = CalculatePathLength(path);

                // Check if this room has a shorter path length
                if (pathLength < closestPathLength && !AllPointsChecked(room))
                {
                    closestPathLengthCandidate = closestPathLength;
                    closestRoomCandidate = currentRoom;

                    closestPathLength = pathLength;
                    currentRoom = room;
                }
                else if (pathLength < closestPathLengthCandidate && !AllPointsChecked(room))
                {
                    closestPathLengthCandidate = pathLength;
                    closestRoomCandidate = room;
                }
            }
        }
        closestRoom = currentRoom;
        return closestRoomCandidate;
    }

    // Helper method to calculate the length of a NavMesh path
    private float CalculatePathLength(NavMeshPath path)
    {
        float length = 0f;

        // Sum up the lengths of each segment in the path
        for (int i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return length;
    }


    private Room CurrentRoom()
    {
        Room currentRoom = null;
        float smallestDistance = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            if (distance < smallestDistance && !AllPointsChecked(room))
            {
                smallestDistance = distance;
                currentRoom = room;
            }
        }

        return currentRoom;
    }

    /*private void Patrol()
    {
        if (!isMoving)
        {
            foreach (PatrolPoints point in closestRoom.patrolPoint)
            {
                if (!point.HasBeenVisited)
                {
                    // Set the flag to indicate that the agent is moving to a patrol point
                    isMoving = true;

                    // Set the destination to the patrol point
                    agent.SetDestination(point.transform.position);

                    // Toggle the check status of the patrol point
                    point.ToggleCheckStatus();

                    // Exit the loop to allow the agent to reach its destination
                    break;
                }
                else
                {
                    if (AllPointsChecked(closestRoom))
                        states = States.SwitchRoom;
                }
            }
        }
        else
        {
            // Check if the agent has reached the patrol point
            if (agent.remainingDistance <= 0.1f)
            {
                // Reset the flag once the agent reaches the patrol point
                isMoving = false;
            }
        }
    }
    */


    private void Patrol()
    {
        if (isMoving && agent.remainingDistance <= 0.3f)
        {
            // Reached the patrol point
            isMoving = false;

            // Mark the patrol point as checked
            if (currentPatrolTarget != null && patrolPointData.ContainsKey(currentPatrolTarget))
            {
                patrolPointData[currentPatrolTarget].lastPatrolTime = Time.time;
                currentPatrolTarget.GetComponent<PatrolPoints>().ToggleCheckStatus();

                patrolPointData[currentPatrolTarget].playerProbability = 0f; // Reset probability after visiting
            }
        }
        if (!isMoving)
        {
            SelectNextPatrolPoint();
        }
    }

    private void Listen()
    {

    }

    private void Chase()
    {

    }


    //---------------------------
    //EVENTS TRIGGERED BY ACTIONS
    //---------------------------
    private void OnSeePlayer(bool canSeePlayer, Vector3 lastSeenTargetLocation)
    {
        if (states != States.ExecuteHPOrder)
        {
            if (canSeePlayer)
            {
                agent.speed = 4f;
                states = States.Chase;
                agent.SetDestination(lastSeenTargetLocation);
                isMoving = true;
                isNotChasing = false; // Disable CommandToMove while chasing
            }
            else if (!isNotChasing)
            {
                if (Vector3.Distance(transform.position, lastSeenTargetLocation) < 1.0f)
                {
                    agent.speed = 3f;
                    states = States.SwitchRoom;
                    isMoving = false;
                    isNotChasing = true; // Enable CommandToMove when not chasing
                    agent.SetDestination(lastSeenTargetLocation);
                }
            }

        }
    }

    private void OnCommandToMove(Vector3 target)
    {
        if (!isNotChasing)
            return;

        // Find room and reset points
        Room targetRoom = FindRoom_Command(target);
        if (targetRoom != null)
            ResetRoomPatrolPoints(targetRoom);

        // --- FIX: Use 'target' directly, not 'targetPos.position' ---
        agent.SetDestination(target);
        states = States.ExecuteOrder;

        // --- NEW: Update probabilities near the director's command location ---
        UpdateProbabilitiesNearLocation(target, 0.5f); // Boost probability by 0.5
    }

    // --- REPLACE your existing OnHighPriorityCommandToMove ---
    private void OnHighPriorityCommandToMove(Vector3 target)
    {
        // Find room and reset points
        Room targetRoom = FindRoom_Command(target);
        if (targetRoom != null)
            ResetRoomPatrolPoints(targetRoom);

        // --- FIX: Use 'target' directly, not 'targetPos.position' ---
        agent.SetDestination(target);
        states = States.ExecuteHPOrder;

        // --- NEW: Update probabilities with a high boost ---
        UpdateProbabilitiesNearLocation(target, 0.75f); // Boost probability by 0.75
    }


    //---------------------
    //PATROL POINT MANAGING
    //---------------------

    // --- ADD THIS ENTIRE METHOD ---
    private void UpdateProbabilitiesNearLocation(Vector3 location, float probabilityBump)
    {
        const float radius = 10f; // Check for patrol points within 10 units of the location

        foreach (var kvp in patrolPointData)
        {
            Transform patrolPoint = kvp.Key;
            HunterPatrolMemory memory = kvp.Value;

            if (Vector3.Distance(patrolPoint.position, location) <= radius)
            {
                memory.playerProbability = Mathf.Clamp01(memory.playerProbability + probabilityBump);
                // Debug.Log($"Boosting probability for {patrolPoint.name} due to Director command.");
            }
        }
    }

    private void OnHunterSawPatrolPoint(Transform patrolPointTransform)
    {
        if (patrolPointData.ContainsKey(patrolPointTransform))
        {
            // Increase the probability for the specific point the Hunter saw
            // You can tune this value (e.g., +0.25f)
            var memory = patrolPointData[patrolPointTransform];
            memory.playerProbability = Mathf.Clamp01(memory.playerProbability + 0.25f);

            // Optional: Log for debugging
            Debug.Log($"Hunter saw {patrolPointTransform.name}, probability is now {memory.playerProbability}");
        }
    }

    private int GetRandomUncheckedPointIndex()
    {
        int randomIndex = 0;
        int bugFix = 0;
        do
        {
            randomIndex = UnityEngine.Random.Range(0, closestRoom.patrolPoint.Length);
            bugFix += 1;

        } while (closestRoom.patrolPoint[randomIndex].GetComponent<PatrolPoints>().HasBeenVisited && bugFix < closestRoom.patrolPoint.Length);
        Debug.Log($"RandomIndex {(randomIndex)}");

        if (!AllPointsChecked(closestRoom))
        {
            randomIndex = -1;
        }

        return randomIndex;
    }

    private bool AllPointsChecked(Room room)
    {
        foreach (PatrolPoints point in room.patrolPoint)
        {
            if (!point.GetComponent<PatrolPoints>().HasBeenVisited)
            {
                return false;
            }
        }
        return true;
    }
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
    private void ResetRoomPatrolPoints(Room room)
    {
        foreach (var point in room.patrolPoint)
        {
            point.ResetCheckStatus();
        }
    }

    //GIZMO DRAWING

    // --- Add these classes inside the Hunter_Basic class or at the bottom of the file ---
    private class PlaceholderTask : Node
    {
        private string taskName;
        public PlaceholderTask(string name) { taskName = name; }
        public override NodeState Evaluate()
        {
            // Debug.Log($"Running Task: {taskName}");
            // Return RUNNING if the task takes time (like movement), SUCCESS if it completes immediately.
            return NodeState.SUCCESS;
        }
    }

    private class PlaceholderCondition : Node
    {
        private string conditionName;
        public PlaceholderCondition(string name) { conditionName = name; }
        public override NodeState Evaluate()
        {
            // Debug.Log($"Checking Condition: {conditionName}");
            // Your actual condition check (e.g., check FOV script)
            return NodeState.FAILURE; // Default to Failure so the Selector moves on
        }
    }
}

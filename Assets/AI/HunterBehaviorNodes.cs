using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using System.Linq;
using static Node;

// Note: You must ensure this script has access to the HunterAI component!

public class HunterBehaviorNodes
{
    private HunterAI hunter;
    private NavMeshAgent agent;

    public HunterBehaviorNodes(HunterAI hunterComponent, NavMeshAgent navAgent)
    {
        this.hunter = hunterComponent;
        this.agent = navAgent;
    }

    // =================================================================
    // BASE CLASSES (Hunter-specific versions of the generic Nodes)
    // =================================================================

    public abstract class HunterTask : Node
    {
        protected HunterBehaviorNodes context;
        public HunterTask(HunterBehaviorNodes context)
        {
            this.context = context;
        }
    }

    public abstract class HunterCondition : Node
    {
        protected HunterBehaviorNodes context;
        public HunterCondition(HunterBehaviorNodes context)
        {
            this.context = context;
        }
    }

    // =================================================================
    // 1. CONDITION: Is Player Visible or Hunter is currently Investigating?
    // =================================================================
    public class IsPlayerSeen : HunterCondition
    {
        public IsPlayerSeen(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            // 1. Check for **Active Sight** (highest priority)
            // We assume the FieldOfView updates the targetPos and the isChasingPlayer flag via the event handler.
            if (context.hunter.timeSinceLastSeen == 0.0f)
            {
                // If the timeSinceLastSeen is 0.0, the player was just seen this frame.
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // 2. Check for **Investigation** (intermediate priority)
            // If we are currently chasing/investigating AND we haven't exceeded the time limit.
            if (context.hunter.isChasingPlayer &&
                context.hunter.timeSinceLastSeen < context.hunter.chaseInvestigationTime)
            {
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // If neither condition is met, the Hunter is not chasing, and the chase branch fails.
            context.hunter.isChasingPlayer = false; // Cleanup flag upon failure
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    // =================================================================
    // 2. TASK: Wander/Roam Locally (Now calls the public GetRandomWanderPoint)
    // =================================================================
    public class WanderLocally : HunterTask
    {
        private float wanderRange = 5f;
        private float acceptableDistance = 0.5f;

        public WanderLocally(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;

            if (agent.remainingDistance <= acceptableDistance && !agent.pathPending)
            {
                // FIX 2: Now works because GetRandomWanderPoint is public in HunterAI
                Vector3 newWanderPoint = context.hunter.GetRandomWanderPoint(agent.transform.position, wanderRange);

                if (newWanderPoint != Vector3.zero)
                {
                    agent.SetDestination(newWanderPoint);
                    nodeState = NodeState.RUNNING;
                    return nodeState;
                }
                else
                {
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                }
            }

            if (agent.hasPath || agent.pathPending)
            {
                nodeState = NodeState.RUNNING;
                return nodeState;
            }

            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    // =================================================================
    // 3. CONDITION: Is Agent At Patrol Point Destination?
    // =================================================================
    // This node is no longer used by our BT structure, but we can leave it.
    public class IsAtDestination : HunterCondition
    {
        private float acceptableDistance = 0.5f;

        public IsAtDestination(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;

            // --- THIS IS THE FIX ---
            // If we don't have a target, we can't be "at" it. This node must fail.
            // This stops the infinite loop with wanderAround.
            if (context.hunter.currentPatrolTarget == null)
            {
                nodeState = NodeState.FAILURE;
                return nodeState;
            }

            if (!agent.pathPending)
            {
                if (agent.remainingDistance <= acceptableDistance)
                {
                    // We have arrived at the currentPatrolTarget.

                    // Record the visit (which sets prob to base)
                    context.hunter.RecordPatrolVisit(context.hunter.currentPatrolTarget);
                    // Clear the target so the BT knows it needs a new one
                    context.hunter.currentPatrolTarget = null;

                    // Return SUCCESS because we *did* successfully arrive.
                    nodeState = NodeState.SUCCESS;
                    return nodeState;
                }
            }

            // If we are not at the destination yet, this condition is a FAILURE.
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    // =================================================================
    // 4. TASK: Move to Patrol Point (Finds a new point OR moves to existing one | Re-evalutation added)
    // =================================================================
    // =================================================================
    // 4. TASK: Move to Patrol Point (Finds a new point OR moves to existing one | Re-evalutation added)
    // =================================================================
    public class MoveToPatrolPoint : HunterTask
    {
        private float acceptableDistance = 1.0f;
        private float velocityStopThreshold = 0.1f;

        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;
            string goalPrefix = "PATROL"; // Default prefix, should be overwritten

            // --- Step 1A: If we have a ShortPatrol goal, set the NEXT point as current target ---
            if (context.hunter.activeGoal != null &&
                context.hunter.activeGoal.type == GoalType.ShortPatrol)
            {
                goalPrefix = "SHORT PATROL";
                if (context.hunter.activeGoal.patrolSteps != null && context.hunter.activeGoal.patrolSteps.Count > 0)
                {
                    // The next target is the FIRST element in the steps list
                    context.hunter.currentPatrolTarget = context.hunter.activeGoal.patrolSteps[0];
                    // Updated log message for target acquisition
                    context.hunter.currentBTState = $"{goalPrefix}: Targeting Step {context.hunter.activeGoal.patrolSteps.Count} Target {context.hunter.currentPatrolTarget.gameObject.name}";
                }
                else
                {
                    // If steps are empty, this patrol step is complete.
                    context.hunter.currentBTState = $"{goalPrefix}: No steps left. Succeeding goal.";
                    return NodeState.SUCCESS;
                }
            }
            // --- Step 1B: If we have a SearchRoom goal, find the best target in that room (EXISTING LOGIC) ---
            else if (context.hunter.activeGoal != null && context.hunter.activeGoal.type == GoalType.SearchRoom)
            {
                goalPrefix = "ROOM SEARCH";
                if (context.hunter.currentPatrolTarget == null)
                {
                    // Find a new target only if the previous one was cleared
                    Transform bestPatrolPoint = context.hunter.GetBestPatrolPoint();

                    if (bestPatrolPoint != null)
                    {
                        context.hunter.currentPatrolTarget = bestPatrolPoint;
                        // Updated log message for target acquisition
                        context.hunter.currentBTState = $"{goalPrefix}: New Target {bestPatrolPoint.gameObject.name}";
                    }
                    else
                    {
                        context.hunter.currentBTState = $"{goalPrefix}: No Points Found / Stuck";
                        nodeState = NodeState.FAILURE; // Cannot move to a non-existent point
                        return nodeState;
                    }
                }
            }
            // --- Step 1C: Safety Check for other goals ---
            else
            {
                // If the goal is not ShortPatrol or SearchRoom, this node doesn't know how to move.
                context.hunter.currentBTState = "MOVE: Invalid Goal Type for Movement.";
                return NodeState.FAILURE;
            }

            // We should have a valid target now (context.hunter.currentPatrolTarget)

            // --- Step 2: We have a valid target, so move towards it. ---
            agent.SetDestination(context.hunter.currentPatrolTarget.position);
            agent.isStopped = false;

            // --- Step 3: Robust Arrival Check ---
            bool isCloseEnough = agent.remainingDistance <= acceptableDistance;
            bool isStoppedMoving = agent.velocity.sqrMagnitude < velocityStopThreshold;
            bool isPathNotPending = !agent.pathPending;

            if (isPathNotPending && isCloseEnough && isStoppedMoving)
            {
                // --- ARRIVAL ACTION ---

                // 1. Record the visit (this sets prob to base for the arrival point)
                context.hunter.RecordPatrolVisit(context.hunter.currentPatrolTarget);

                // 2. Consume the patrol point if it was part of a ShortPatrol
                if (context.hunter.activeGoal.type == GoalType.ShortPatrol &&
                    context.hunter.activeGoal.patrolSteps.Count > 0)
                {
                    context.hunter.activeGoal.patrolSteps.RemoveAt(0);
                }

                // 3. Clear the current target 
                context.hunter.currentPatrolTarget = null;

                // Updated log message for arrival
                context.hunter.currentBTState = $"{goalPrefix}: Arrived at {context.hunter.currentPatrolTarget.name}";
                nodeState = NodeState.SUCCESS; // We are done moving to this step.
                return nodeState;
            }

            // If we are not at the destination, we are still RUNNING.
            // Updated log message for running
            context.hunter.currentBTState = $"{goalPrefix}: Moving to {context.hunter.currentPatrolTarget.name}";
            nodeState = NodeState.RUNNING;
            return nodeState;
        }
    }

    // =================================================================
    // 5. TASK: Chase Player (High Priority Action)
    // =================================================================
    public class ChasePlayer : HunterTask
    {
        private float acceptableDistance = 1.0f;
        float velocityStopThreshold = 0.1f;

        public ChasePlayer(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;
            Transform targetTransform = context.hunter.targetPos;

            if (targetTransform == null)
            {
                return NodeState.FAILURE;
            }

            // Set or Maintain Destination
            agent.SetDestination(targetTransform.position);
            agent.isStopped = false; // Ensure movement is active

            // ROBUST ARRIVAL CHECK:
            // 1. Are we close enough? (remainingDistance)
            bool isCloseEnough = agent.remainingDistance <= acceptableDistance;
            // 2. Are we physically stopped? (This is the most reliable check to avoid orbiting/flickering)
            bool isStoppedMoving = agent.velocity.sqrMagnitude < velocityStopThreshold;
            // 3. Is the path calculation complete? (Path must not be pending)
            bool isPathNotPending = !agent.pathPending;

            // Check for Arrival
            bool hasArrived = isCloseEnough && isStoppedMoving && isPathNotPending;

            if (hasArrived)
            {

                agent.isStopped = true;

                // Return RUNNING to stall the BT on this node.
                // This holds the highest-priority branch open while the Hunter waits 
                // for the 7-second investigation timer (managed in HunterAI.cs) to expire.
                context.hunter.currentBTState = "CHASING/INVESTIGATING: Arrived, Waiting for Timer"; // Update debug state


                nodeState = NodeState.RUNNING;
                return nodeState;
            }
            else
            {
                // If we are still moving, ensure the agent is active
                agent.isStopped = false;
                context.hunter.currentBTState = "CHASING/INVESTIGATING: Moving to Last Seen"; // Update debug state
            }

            // Chase is Running (still moving to the last known spot)
            nodeState = NodeState.RUNNING;
            return nodeState;
        }
    }

    // =================================================================
    // --- "SMART" INVESTIGATION NODE ---
    // =================================================================

    public class ObserveAndScan : HunterTask
    {
        private float totalScanDuration = 8f; // Max failsafe time
        private float totalTimeElapsed = 0f;
        private float scanPointTimer = 0f;

        private float currentScanTimeLimit = 2.0f;
        private float rotationSpeed = 3.0f; // Faster head turn for short patrol

        private List<HunterPatrolMemory> hotPoints;
        private HunterPatrolMemory currentScanTarget;

        private bool hasFinishedHotScan = false;
        private bool hasPerformedParanoiaCheck = false;

        public ObserveAndScan(HunterBehaviorNodes context) : base(context) { }

        private void OnEnter()
        {
            // 1. Stop the agent so we can rotate/look around
            context.agent.isStopped = true;
            context.hunter.currentBTState = "INVESTIGATE: Stopping to scan...";

            // 2. Decide WHAT to scan based on the Goal
            if (context.hunter.activeGoal != null)
            {
                if (context.hunter.activeGoal.type == GoalType.ShortPatrol)
                {
                    // NEW: If Short Patrol, look at things nearby (Spatial Awareness)
                    // Range: 10 meters. This makes him "notice" things he passed by.
                    hotPoints = context.hunter.GetLocalHotPoints(context.agent.transform.position, 10f);
                    context.hunter.currentBTState = $"INVESTIGATE: Short Patrol Scan. Found {hotPoints.Count} nearby points.";

                    // Reduce duration for short patrol so it feels snappy (Move > Glance > Move)
                    totalScanDuration = 4.0f;
                }
                else if (context.hunter.activeGoal.type == GoalType.SearchRoom && context.hunter.activeGoal.targetRoom != null)
                {
                    // OLD: If Search Room, look at the whole room list
                    hotPoints = context.hunter.GetHotPointsInRoom(context.hunter.activeGoal.targetRoom);
                    totalScanDuration = 8.0f; // Full room search takes longer
                }
                else
                {
                    hotPoints = new List<HunterPatrolMemory>();
                }
            }
            else
            {
                hotPoints = new List<HunterPatrolMemory>();
            }

            // 3. Reset timers
            totalTimeElapsed = 0f;
            scanPointTimer = 0f;
            currentScanTarget = null;
            hasFinishedHotScan = false;
            hasPerformedParanoiaCheck = false;
        }

        private void OnExit()
        {
            context.agent.isStopped = false;
            // Do NOT clear currentPatrolTarget here, MoveToPatrolPoint handles the next step from the list
            hotPoints = null;
            currentScanTarget = null;
        }

        public override NodeState Evaluate()
        {
            if (totalTimeElapsed == 0f) OnEnter();

            totalTimeElapsed += Time.deltaTime;

            if (totalTimeElapsed >= totalScanDuration)
            {
                context.hunter.currentBTState = "INVESTIGATE: Scan time over. Continuing...";
                OnExit();
                return NodeState.SUCCESS;
            }

            // --- LOOKING LOGIC ---
            if (currentScanTarget != null)
            {
                scanPointTimer += Time.deltaTime;

                // MECHANIC: Physically rotate the agent to face the target
                // This allows the FieldOfView cone to "sweep" over the target, triggering the Glance Perk
                Vector3 direction = currentScanTarget.patrolpointTransform.position - context.agent.transform.position;
                direction.y = 0;
                if (direction != Vector3.zero)
                {
                    Quaternion lookRotation = Quaternion.LookRotation(direction);
                    context.agent.transform.rotation = Quaternion.Slerp(
                        context.agent.transform.rotation,
                        lookRotation,
                        Time.deltaTime * rotationSpeed
                    );
                }

                // Check if done looking
                bool isTimerDone = scanPointTimer >= currentScanTimeLimit;
                bool isPointCooled = currentScanTarget.playerProbability <= context.hunter.baseUncertainty;

                if (isTimerDone || isPointCooled)
                {
                    currentScanTarget = null;
                    scanPointTimer = 0f;
                }

                return NodeState.RUNNING;
            }

            // --- GET NEW LOOK TARGET ---
            if (!hasFinishedHotScan)
            {
                if (hotPoints != null && hotPoints.Count > 0)
                {
                    currentScanTarget = hotPoints[0];
                    hotPoints.RemoveAt(0);
                    // Quick glance (1-2 seconds)
                    currentScanTimeLimit = UnityEngine.Random.Range(1.0f, 2.0f);
                    context.hunter.currentBTState = $"INVESTIGATE: Glancing at {currentScanTarget.patrolpointTransform.name}";
                    return NodeState.RUNNING;
                }
                else
                {
                    hasFinishedHotScan = true;
                }
            }

            // If we are done scanning hot points, we can finish early
            OnExit();
            return NodeState.SUCCESS;
        }
    }


    // =================================================================
    // 6. CONDITION: Does the Hunter have an active, valid goal?
    // =================================================================
    public class HasActiveGoal : HunterCondition
    {
        public HasActiveGoal(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            // 1. Check if we have a goal at all.
            if (context.hunter.activeGoal == null || context.hunter.activeGoal.type == GoalType.None)
            {
                nodeState = NodeState.FAILURE;
                return nodeState;
            }

            // 2. Check for **ShortPatrol** Goal Completion (Changelog 191125)
            if (context.hunter.activeGoal.type == GoalType.ShortPatrol)
            {
                if (context.hunter.activeGoal.patrolSteps == null || context.hunter.activeGoal.patrolSteps.Count == 0)
                {
                    // This goal is complete! Clear it and FAIL.
                    context.hunter.currentBTState = "PLANNER: ShortPatrol complete.";
                    context.hunter.activeGoal = null;
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                }
            }

            // 3. Check if the goal is a "SearchRoom" goal that is already complete.
            if (context.hunter.activeGoal.type == GoalType.SearchRoom)
            {
                if (context.hunter.activeGoal.targetRoom == null)
                {
                    // Goal is invalid.
                    context.hunter.activeGoal = null;
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                }

                // Check if the room is "cold."
                if (context.hunter.activeGoal.targetRoom.IsFullyPatrolled(context.hunter.baseUncertainty))
                {
                    // This goal is complete! Clear it and FAIL,
                    // so the Planner runs to find a *new* goal.
                    context.hunter.currentBTState = $"PLANNER: Goal for {context.hunter.activeGoal.targetRoom.roomName} is complete.";
                    context.hunter.activeGoal = null;
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                }
            }

            // --- (Future) ---
            // You could add checks for other goal types here,
            // like GoalType.Ambush, and check if its timer has expired.

            // 4. If we are here, we have a valid, active goal.
            nodeState = NodeState.SUCCESS;
            return nodeState;
        }
    }

    // =================================================================
    // 7. TASK: Planner finds a new goal (High-level decision)
    // =================================================================

    public class Planner_FindNewGoal : HunterTask
    {
        // Path cost penalty. Higher = distance matters more.
        private float pathCostPenalty = 0.5f;

        public Planner_FindNewGoal(HunterBehaviorNodes context) : base(context) { }

        // Helper function to get all unpatrolled rooms
        private List<RoomInfo> GetUnpatrolledRooms()
        {
            List<RoomInfo> unpatrolledRooms = new List<RoomInfo>();
            foreach (RoomInfo room in context.hunter.roomData.Values)
            {
                if (!room.IsFullyPatrolled(context.hunter.baseUncertainty))
                {
                    unpatrolledRooms.Add(room);
                }
            }
            return unpatrolledRooms;
        }

        // Helper function to calculate path cost to a room
        private float GetPathCostToRoom(RoomInfo room)
        {
            if (room.roomRef == null) return float.PositiveInfinity; // Safety check

            NavMeshPath path = new NavMeshPath();
            if (context.agent.CalculatePath(room.roomRef.transform.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                // Call the public function you created in HunterAI.cs
                return context.hunter.CalculatePathCost(path);
            }
            return float.PositiveInfinity; // Unreachable
        }

        // ... (start of Planner_FindNewGoal)

        public override NodeState Evaluate()
        {
            context.hunter.currentBTState = "PLANNER: Assessing... Looking for new goal.";

            RoomInfo bestRoom = null;
            float maxScore = float.NegativeInfinity;

            // --- PRIORITY 1: Generate a SHORT PATROL Route (NEW Highest Priority Planning) ---
            // If the Hunter is mobile and can find a hot path, this is the preferred goal.
            List<Transform> bestRoute = context.hunter.GetBestPatrolRoute(4); // Find a route of max 4 steps

            if (bestRoute != null && bestRoute.Count > 0)
            {
                // For now, if a route is found, we take it as the highest value decision.
                context.hunter.activeGoal = new HunterGoal(GoalType.ShortPatrol, steps: bestRoute);
                context.hunter.currentBTState = $"PLANNER: New Goal - ShortPatrol of {bestRoute.Count} steps.";
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // --- PRIORITY 2: Fallback to Search Hottest "Un-patrolled" Room (EXISTING LOGIC) ---
            List<RoomInfo> unpatrolledRooms = GetUnpatrolledRooms();

            // If we have "hot" rooms, pick the best one
            if (unpatrolledRooms.Count > 0)
            {
                foreach (RoomInfo room in unpatrolledRooms)
                {
                    float pathCost = GetPathCostToRoom(room);
                    if (pathCost == float.PositiveInfinity) continue; // Skip unreachable

                    // --- THIS IS THE FIX FOR BUG 2 (Distance) ---
                    // Score = (How "hot" is the room) - (How "far" is the room)
                    float score = (room.generalCuriosity * 100f) - (pathCost * pathCostPenalty);

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestRoom = room;
                    }
                }

                if (bestRoom != null)
                {
                    // We found a "hot" room!
                    context.hunter.activeGoal = new HunterGoal(GoalType.SearchRoom, bestRoom);
                    context.hunter.currentBTState = $"PLANNER: New Goal - Search {bestRoom.roomName} (Score: {maxScore:F0})";
                    nodeState = NodeState.SUCCESS;
                    return nodeState;
                }
            }

            // --- Priority 2: Failsafe "Wander" (All rooms are "cold") ---
            // This code only runs if *all* rooms are "cold"
            // We find the "least-worst" cold room to re-check (the one with the highest heat)
            // This fixes the "stuck loop" (Bug 1)
            context.hunter.currentBTState = "PLANNER: All rooms 'cold'. Finding least-recently-visited.";

            maxScore = float.NegativeInfinity; // Reset score
            bestRoom = null; // Reset room

            foreach (RoomInfo room in context.hunter.roomData.Values)
            {
                float pathCost = GetPathCostToRoom(room);
                if (pathCost == float.PositiveInfinity) continue; // Skip unreachable

                // Same scoring, but now we're comparing "cold" rooms.
                // The one with the highest curiosity (from the timer) will win.
                float score = (room.generalCuriosity * 100f) - (pathCost * pathCostPenalty);

                if (score > maxScore)
                {
                    maxScore = score;
                    bestRoom = room;
                }
            }

            if (bestRoom != null)
            {
                // We found the "least-worst" cold room to re-check
                context.hunter.activeGoal = new HunterGoal(GoalType.SearchRoom, bestRoom);
                context.hunter.currentBTState = $"PLANNER: Bored. Re-checking {bestRoom.roomName} (Score: {maxScore:F0})";
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // This should only happen if there are no rooms or no reachable rooms
            context.hunter.currentBTState = "PLANNER: FAILED. No reachable rooms in memory.";
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }
}



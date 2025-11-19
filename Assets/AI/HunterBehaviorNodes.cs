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

            // If we don't have a target, we can't be "at" it. This node must fail.
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
    // 4. TASK: Move to Patrol Point (Smart Movement - No Lists)
    // =================================================================
    public class MoveToPatrolPoint : HunterTask
    {
        private float acceptableDistance = 1.0f;
        private float velocityStopThreshold = 0.1f;

        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;
            string goalPrefix = "PATROL";

            // --- Step 1: If we don't have a target, FIND ONE based on the Goal Type ---
            if (context.hunter.currentPatrolTarget == null)
            {
                if (context.hunter.activeGoal != null)
                {
                    // A. FREE PATROL: Find the best single point nearby (Heat - Distance)
                    if (context.hunter.activeGoal.type == GoalType.FreePatrol)
                    {
                        goalPrefix = "FREE PATROL";
                        // Use the new simple helper
                        Transform bestNearby = context.hunter.GetBestNextPoint(context.agent.transform.position);

                        if (bestNearby != null)
                        {
                            context.hunter.currentPatrolTarget = bestNearby;
                            context.hunter.currentBTState = $"{goalPrefix}: Targeting {bestNearby.name} (Reactive)";
                        }
                        else
                        {
                            // If no points found nearby, Free Patrol fails (triggering Planner to run again)
                            context.hunter.currentBTState = $"{goalPrefix}: No hot points found nearby.";
                            return NodeState.FAILURE;
                        }
                    }
                    // B. SEARCH ROOM: Find the best point inside the specific room
                    else if (context.hunter.activeGoal.type == GoalType.SearchRoom)
                    {
                        goalPrefix = "ROOM SEARCH";
                        Transform bestInRoom = context.hunter.GetBestPatrolPointInRoom(context.hunter.activeGoal.targetRoom);

                        if (bestInRoom != null)
                        {
                            context.hunter.currentPatrolTarget = bestInRoom;
                            context.hunter.currentBTState = $"{goalPrefix}: Targeting {bestInRoom.name}";
                        }
                        else
                        {
                            context.hunter.currentBTState = $"{goalPrefix}: Room seems empty/cleared.";
                            return NodeState.FAILURE;
                        }
                    }
                }
                else
                {
                    return NodeState.FAILURE; // No Goal
                }
            }

            // Safety Check
            if (context.hunter.currentPatrolTarget == null) return NodeState.FAILURE;


            // --- Step 2: Move towards target ---
            agent.SetDestination(context.hunter.currentPatrolTarget.position);
            agent.isStopped = false;

            // --- Step 3: Robust Arrival Check ---
            bool isCloseEnough = agent.remainingDistance <= acceptableDistance;
            bool isStoppedMoving = agent.velocity.sqrMagnitude < velocityStopThreshold;
            bool isPathNotPending = !agent.pathPending;

            if (isPathNotPending && isCloseEnough && isStoppedMoving)
            {
                // --- ARRIVAL ACTION ---

                // 1. Record the visit (This cools the point down)
                context.hunter.RecordPatrolVisit(context.hunter.currentPatrolTarget);

                // 2. Clear the current target immediately.
                // This forces Step 1 to run again in the next loop, calculating the *next* best step.
                context.hunter.currentPatrolTarget = null;

                context.hunter.currentBTState = $"{goalPrefix}: Arrived. Scanning next...";
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // If we are not at the destination, we are still RUNNING.
            //context.hunter.currentBTState = $"{goalPrefix}: Moving to [{context.hunter.currentPatrolTarget.roomName}] {context.hunter.currentPatrolTarget.name}";
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

            if (targetTransform == null) return NodeState.FAILURE;

            agent.SetDestination(targetTransform.position);
            agent.isStopped = false;

            bool isCloseEnough = agent.remainingDistance <= acceptableDistance;
            bool isStoppedMoving = agent.velocity.sqrMagnitude < velocityStopThreshold;
            bool isPathNotPending = !agent.pathPending;

            bool hasArrived = isCloseEnough && isStoppedMoving && isPathNotPending;

            if (hasArrived)
            {
                agent.isStopped = true;
                context.hunter.currentBTState = "CHASING/INVESTIGATING: Arrived, Waiting for Timer";
                nodeState = NodeState.RUNNING;
                return nodeState;
            }
            else
            {
                agent.isStopped = false;
                context.hunter.currentBTState = "CHASING/INVESTIGATING: Moving to Last Seen";
            }

            nodeState = NodeState.RUNNING;
            return nodeState;
        }
    }

    // =================================================================
    // 6. TASK: "SMART" INVESTIGATION NODE
    // =================================================================

    public class ObserveAndScan : HunterTask
    {
        private float totalScanDuration = 8f;
        private float totalTimeElapsed = 0f;
        private float scanPointTimer = 0f;

        private float currentScanTimeLimit = 2.0f;
        private float rotationSpeed = 3.0f;

        private List<HunterPatrolMemory> hotPoints;
        private HunterPatrolMemory currentScanTarget;

        private bool hasFinishedHotScan = false;

        public ObserveAndScan(HunterBehaviorNodes context) : base(context) { }

        private void OnEnter()
        {
            // 1. Stop the agent so we can rotate/look around
            context.agent.isStopped = true;

            // 2. Decide WHAT to scan based on the Goal
            if (context.hunter.activeGoal != null)
            {
                // A. FREE PATROL: Look at nearby things (Radius Check)
                if (context.hunter.activeGoal.type == GoalType.FreePatrol)
                {
                    // Check 10m radius for anything interesting
                    hotPoints = context.hunter.GetLocalHotPoints(context.agent.transform.position, 10f);
                    context.hunter.currentBTState = $"INVESTIGATE: Free Patrol Scan. Found {hotPoints.Count} nearby points.";
                    totalScanDuration = 4.0f; // Snappy scan
                }
                // B. ROOM SEARCH: Look at the room's list
                else if (context.hunter.activeGoal.type == GoalType.SearchRoom && context.hunter.activeGoal.targetRoom != null)
                {
                    hotPoints = context.hunter.GetHotPointsInRoom(context.hunter.activeGoal.targetRoom);
                    totalScanDuration = 8.0f; // Systematic search
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
        }

        private void OnExit()
        {
            context.agent.isStopped = false;
            hotPoints = null;
            currentScanTarget = null;
        }

        public override NodeState Evaluate()
        {
            if (totalTimeElapsed == 0f) OnEnter();

            totalTimeElapsed += Time.deltaTime;

            // Timeout check
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

                // Check if done looking (Time OR Cool-down logic)
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
                    // Random glance duration
                    currentScanTimeLimit = UnityEngine.Random.Range(1.0f, 2.0f);
                    context.hunter.currentBTState = $"INVESTIGATE: Glancing at {currentScanTarget.patrolpointTransform.name}";
                    return NodeState.RUNNING;
                }
                else
                {
                    hasFinishedHotScan = true;
                }
            }

            // If we are done scanning hot points, finish early
            OnExit();
            return NodeState.SUCCESS;
        }
    }


    // =================================================================
    // 7. CONDITION: Does the Hunter have an active, valid goal?
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

            // 2. Check Free Patrol
            if (context.hunter.activeGoal.type == GoalType.FreePatrol)
            {
                // Free Patrol is considered "Active" as long as the Planner hasn't replaced it.
                // It relies on MoveToPatrolPoint returning Failure if it runs out of points.
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // 3. Check Search Room Completion
            if (context.hunter.activeGoal.type == GoalType.SearchRoom)
            {
                if (context.hunter.activeGoal.targetRoom == null)
                {
                    context.hunter.activeGoal = null;
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                }

                // Check if the room is "cold."
                if (context.hunter.activeGoal.targetRoom.IsFullyPatrolled(context.hunter.baseUncertainty))
                {
                    context.hunter.currentBTState = $"PLANNER: {context.hunter.activeGoal.targetRoom.roomName} is clear.";
                    context.hunter.activeGoal = null;
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                }
            }

            // 4. If we are here, we have a valid goal.
            nodeState = NodeState.SUCCESS;
            return nodeState;
        }
    }

    // =================================================================
    // 8. TASK: Planner finds a new goal (High-level decision)
    // =================================================================

    public class Planner_FindNewGoal : HunterTask
    {
        private float pathCostPenalty = 0.5f;

        public Planner_FindNewGoal(HunterBehaviorNodes context) : base(context) { }

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

        private float GetPathCostToRoom(RoomInfo room)
        {
            if (room.roomRef == null) return float.PositiveInfinity;

            NavMeshPath path = new NavMeshPath();
            if (context.agent.CalculatePath(room.roomRef.transform.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                return context.hunter.CalculatePathCost(path);
            }
            return float.PositiveInfinity;
        }

        public override NodeState Evaluate()
        {
            context.hunter.currentBTState = "PLANNER: Assessing...";

            // --- PRIORITY 1: Check for Free Patrol Opportunities (Local Hot Points) ---
            // Instead of calculating a route, we just check if there is *any* good next step nearby.
            Transform bestNearby = context.hunter.GetBestNextPoint(context.agent.transform.position);

            if (bestNearby != null)
            {
                // If there's a good point nearby, we default to Free Patrol.
                // This keeps the Hunter mobile and reactive.
                context.hunter.activeGoal = new HunterGoal(GoalType.FreePatrol);
                context.hunter.currentBTState = "PLANNER: Assigned Free Patrol (Hot points nearby).";
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // --- PRIORITY 2: Search Hottest "Un-patrolled" Room (Long Distance) ---
            // If nothing is nearby, we look for a specific room to travel to.
            RoomInfo bestRoom = null;
            float maxScore = float.NegativeInfinity;
            List<RoomInfo> unpatrolledRooms = GetUnpatrolledRooms();

            if (unpatrolledRooms.Count > 0)
            {
                foreach (RoomInfo room in unpatrolledRooms)
                {
                    float pathCost = GetPathCostToRoom(room);
                    if (pathCost == float.PositiveInfinity) continue;

                    // Score = Heat - Distance
                    float score = (room.generalCuriosity * 100f) - (pathCost * pathCostPenalty);

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestRoom = room;
                    }
                }

                if (bestRoom != null)
                {
                    context.hunter.activeGoal = new HunterGoal(GoalType.SearchRoom, bestRoom);
                    context.hunter.currentBTState = $"PLANNER: Traveling to {bestRoom.roomName} (Score: {maxScore:F0})";
                    nodeState = NodeState.SUCCESS;
                    return nodeState;
                }
            }

            // --- Priority 3: Failsafe (Boredom) ---
            // All rooms are cold, and nothing is nearby. Find the "least worst" room.
            context.hunter.currentBTState = "PLANNER: Bored. Finding least-recently-visited.";
            maxScore = float.NegativeInfinity;
            bestRoom = null;

            foreach (RoomInfo room in context.hunter.roomData.Values)
            {
                float pathCost = GetPathCostToRoom(room);
                if (pathCost == float.PositiveInfinity) continue;

                float score = (room.generalCuriosity * 100f) - (pathCost * pathCostPenalty);
                if (score > maxScore)
                {
                    maxScore = score;
                    bestRoom = room;
                }
            }

            if (bestRoom != null)
            {
                context.hunter.activeGoal = new HunterGoal(GoalType.SearchRoom, bestRoom);
                context.hunter.currentBTState = $"PLANNER: Re-checking {bestRoom.roomName}";
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            context.hunter.currentBTState = "PLANNER: FAILED. No reachable rooms.";
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static Node;

public class HunterBehaviorNodes
{
    private HunterAI hunter;
    private NavMeshAgent agent;

    public HunterBehaviorNodes(HunterAI hunterComponent, NavMeshAgent navAgent)
    {
        this.hunter = hunterComponent;
        this.agent = navAgent;
    }

    #region --- BASE CLASSES ---

    public abstract class HunterTask : Node
    {
        protected HunterBehaviorNodes context;
        public HunterTask(HunterBehaviorNodes context) => this.context = context;
    }

    public abstract class HunterCondition : Node
    {
        protected HunterBehaviorNodes context;
        public HunterCondition(HunterBehaviorNodes context) => this.context = context;
    }

    #endregion

    #region --- CHASE BRANCH ---

    // 1. CONDITION: IS PLAYER SEEN?
    public class IsPlayerSeen : HunterCondition
    {
        public IsPlayerSeen(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            // Immediate Sighting
            if (context.hunter.timeSinceLastSeen == 0.0f) return NodeState.SUCCESS;

            // Investigation Timer Active
            if (context.hunter.isChasingPlayer &&
                context.hunter.timeSinceLastSeen < context.hunter.chaseInvestigationTime)
            {
                return NodeState.SUCCESS;
            }

            // Lost
            context.hunter.isChasingPlayer = false;
            return NodeState.FAILURE;
        }
    }

    // 2. ACTION: CHASE MOVEMENT
    public class ChasePlayer : HunterTask
    {
        private float acceptableDistance = 1.0f;
        private float velocityStopThreshold = 0.1f;

        public ChasePlayer(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            NavMeshAgent agent = context.agent;
            Transform targetTransform = context.hunter.targetPos;

            if (targetTransform == null) return NodeState.FAILURE;

            agent.SetDestination(targetTransform.position);
            agent.isStopped = false;

            bool isCloseEnough = agent.remainingDistance <= acceptableDistance;
            bool isStoppedMoving = agent.velocity.sqrMagnitude < velocityStopThreshold;
            bool isPathNotPending = !agent.pathPending;

            if (isPathNotPending && isCloseEnough && isStoppedMoving)
            {
                agent.isStopped = true;
                context.hunter.currentBTState = "CHASING: Investigating Last Seen...";
                return NodeState.RUNNING; // Stay here until timer expires
            }
            else
            {
                agent.isStopped = false;
                context.hunter.currentBTState = "CHASING: Moving to Last Seen...";
            }

            return NodeState.RUNNING;
        }
    }

    #endregion

    #region --- PATROL MOVEMENT BRANCH ---

    // 3. ACTION: ACQUIRE TARGET (The Brain Connector)
    // Calculates Vantage Points and locks onto targets.
    public class AcquirePatrolTarget : HunterTask
    {
        public AcquirePatrolTarget(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            // A. Check Existing Target validity
            if (context.hunter.currentInterestTarget != null)
            {
                // Check Distance to VANTAGE POINT (Not Object)
                float dist = Vector3.Distance(context.agent.transform.position, context.hunter.currentNavDestination);

                // If close to vantage point, allow Action to run
                if (dist <= 1.5f) return NodeState.SUCCESS;

                if (context.hunter.patrolPointData.TryGetValue(context.hunter.currentInterestTarget, out HunterPatrolMemory mem))
                {
                    // Commitment: Special points must be visited
                    if (mem.pointType != PointType.Standard) return NodeState.SUCCESS;

                    // Standard: Drop if cooled visually
                    if (mem.playerProbability <= context.hunter.baseUncertainty + 0.05f)
                    {
                        context.hunter.currentBTState = $"PATROL: {context.hunter.currentInterestTarget.name} cooled. Switching...";
                        context.hunter.currentInterestTarget = null;
                    }
                    else
                    {
                        return NodeState.SUCCESS;
                    }
                }
            }

            // B. Find New Target
            Transform bestPoint = context.hunter.GetBestNextPoint(context.agent.transform.position);

            if (bestPoint != null)
            {
                context.hunter.currentInterestTarget = bestPoint;

                // VANTAGE CALCULATION (Stalker Logic)
                context.hunter.currentNavDestination = VantageSolver.GetVantagePosition(bestPoint, context.agent.transform.position, 3.0f);

                string roomName = "Hallway";
                if (context.hunter.patrolPointData.TryGetValue(bestPoint, out HunterPatrolMemory mem) && mem.parentRoom != null)
                {
                    roomName = mem.parentRoom.roomName;
                }

                context.hunter.currentBTState = $"PATROL: Stalking [{roomName}] {bestPoint.name}";
                return NodeState.SUCCESS;
            }

            context.hunter.currentBTState = "PATROL: No valid targets found (Idle)";
            return NodeState.FAILURE;
        }
    }

    // 4. ACTION: MOVE TO VANTAGE POINT
    public class MoveToPatrolPoint : HunterTask
    {
        private float switchDistance = 1.5f; // Tighter for Vantage Points

        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            if (context.hunter.currentInterestTarget == null) return NodeState.FAILURE;

            NavMeshAgent agent = context.agent;

            // Move to VANTAGE point
            agent.SetDestination(context.hunter.currentNavDestination);
            agent.isStopped = false;

            float dist = Vector3.Distance(agent.transform.position, context.hunter.currentNavDestination);

            if (dist <= switchDistance) return NodeState.SUCCESS;

            return NodeState.RUNNING;
        }
    }

    #endregion

    #region --- CONTEXT ACTIONS BRANCH ---

    // 5. CONDITION: CHECK TYPE
    public class IsPatrolPointType : HunterCondition
    {
        private PointType targetType;

        public IsPatrolPointType(HunterBehaviorNodes context, PointType type) : base(context)
        {
            this.targetType = type;
        }

        protected override NodeState OnUpdate()
        {
            if (context.hunter.currentInterestTarget == null) return NodeState.FAILURE;

            if (context.hunter.patrolPointData.TryGetValue(context.hunter.currentInterestTarget, out HunterPatrolMemory mem))
            {
                if (mem.pointType == targetType) return NodeState.SUCCESS;
            }

            return NodeState.FAILURE;
        }
    }

    // 6. ACTION: PEEK / CREEP (Partner Point Version)
    public class PerformDoorwayPeek : HunterTask
    {
        // State
        private float timer = 0f;
        private float originalSpeed;
        private float originalStoppingDist;
        private bool isValidTarget;
        private Vector3 debugTargetPos;

        // Settings
        private float creepSpeed = 0.5f;
        private float creepDistance = 1.5f; // Used for backup calc
        private float peekDuration = 4.5f;

        public PerformDoorwayPeek(HunterBehaviorNodes context) : base(context) { }

        protected override void OnEnter()
        {
            NavMeshAgent agent = context.agent;

            // 1. Capture State
            originalSpeed = agent.speed;
            originalStoppingDist = agent.stoppingDistance;

            // 2. Apply Slow Settings
            agent.speed = creepSpeed;
            agent.stoppingDistance = 0.1f;
            agent.isStopped = false;
            isValidTarget = false;

            // 3. Calculate Creep Target (Partner Point Logic)
            if (context.hunter.currentInterestTarget != null)
            {
                Vector3 startPos = context.hunter.currentInterestTarget.position;
                Vector3 forwardDir = context.hunter.currentInterestTarget.forward;

                // Default: Lifted Forward Vector
                Vector3 rawTarget = startPos + (forwardDir * 1.5f);

                // Try to use Partner Point (Preferred)
                PatrolPoints pointScript = context.hunter.currentInterestTarget.GetComponent<PatrolPoints>();
                if (pointScript != null && pointScript.partnerPoint != null)
                {
                    rawTarget = pointScript.partnerPoint.transform.position;
                }

                // Lift & Snap (The Floor Fix)
                NavMeshHit hit;
                if (NavMesh.SamplePosition(rawTarget + Vector3.up * 0.5f, out hit, 3.0f, NavMesh.AllAreas))
                {
                    // Check Path Validity
                    if (context.hunter.IsPathValid(hit.position, out float cost))
                    {
                        agent.SetDestination(hit.position);
                        debugTargetPos = hit.position;
                        isValidTarget = true;
                    }
                    else
                    {
                        // Fallback: Stand at threshold
                        agent.SetDestination(startPos);
                    }
                }
                else
                {
                    agent.SetDestination(startPos);
                }
            }

            timer = peekDuration;
            context.hunter.currentBTState = "ACTION: Creeping into Room...";
        }

        protected override NodeState OnUpdate()
        {
            // Debug Visuals
            Color color = isValidTarget ? Color.green : Color.red;
            if (isValidTarget && !context.agent.hasPath && !context.agent.pathPending) color = Color.yellow;
            Debug.DrawLine(context.agent.transform.position, debugTargetPos, color);

            timer -= Time.deltaTime;

            if (timer > 0f) return NodeState.RUNNING;

            return NodeState.SUCCESS;
        }

        protected override void OnExit()
        {
            // Restore State
            context.agent.speed = originalSpeed;
            context.agent.stoppingDistance = originalStoppingDist;

            // Mark Visited & Cool
            if (timer <= 0f && context.hunter.currentInterestTarget != null)
            {
                context.hunter.RecordPatrolVisit(context.hunter.currentInterestTarget);
                context.hunter.currentInterestTarget = null;
            }
        }
    }

    // 7. ACTION: STANDARD (Walk-by)
    public class PerformStandardAction : HunterTask
    {
        public PerformStandardAction(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            if (context.hunter.currentInterestTarget != null)
            {
                context.hunter.RecordPatrolVisit(context.hunter.currentInterestTarget);
                context.hunter.currentInterestTarget = null;
            }
            return NodeState.SUCCESS;
        }
    }

    #endregion
}
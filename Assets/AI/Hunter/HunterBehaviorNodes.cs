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

    // =================================================================
    // BASE CLASSES
    // =================================================================
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

    // =================================================================
    // 1. CHASE BRANCH
    // =================================================================
    public class IsPlayerSeen : HunterCondition
    {
        public IsPlayerSeen(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            if (context.hunter.timeSinceLastSeen == 0.0f) return NodeState.SUCCESS;

            if (context.hunter.isChasingPlayer &&
                context.hunter.timeSinceLastSeen < context.hunter.chaseInvestigationTime)
            {
                return NodeState.SUCCESS;
            }

            context.hunter.isChasingPlayer = false;
            return NodeState.FAILURE;
        }
    }

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
                return NodeState.RUNNING;
            }
            else
            {
                agent.isStopped = false;
                context.hunter.currentBTState = "CHASING: Moving to Last Seen...";
            }

            return NodeState.RUNNING;
        }
    }

    // =================================================================
    // 2. PATROL MOVEMENT
    // =================================================================
    public class AcquirePatrolTarget : HunterTask
    {
        public AcquirePatrolTarget(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            // 1. Check if we have a valid target
            if (context.hunter.currentInterestTarget != null)
            {
                float dist = Vector3.Distance(context.agent.transform.position, context.hunter.currentNavDestination);

                if (dist <= 3.0f) return NodeState.SUCCESS;

                if (context.hunter.patrolPointData.TryGetValue(context.hunter.currentInterestTarget, out HunterPatrolMemory mem))
                {
                    // COMMITMENT LOGIC
                    if (mem.pointType != PointType.Standard) return NodeState.SUCCESS;

                    // CORNER CUTTING
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

            // 2. Find BEST next target
            // Note: GetBestNextPoint now returns the Interest Target (Transform).
            Transform bestPoint = context.hunter.GetBestNextPoint(context.agent.transform.position);

            if (bestPoint != null)
            {
                context.hunter.currentInterestTarget = bestPoint;

                // CALC VANTAGE POINT (Destination)
                // Use the VantageSolver to find where to stand
                context.hunter.currentNavDestination = VantageSolver.GetVantagePosition(bestPoint, context.agent.transform.position, 3.0f);

                string roomName = "Hallway";
                if (context.hunter.patrolPointData.TryGetValue(bestPoint, out HunterPatrolMemory mem) && mem.parentRoom != null)
                {
                    roomName = mem.parentRoom.roomName;
                }
                context.hunter.currentBTState = $"PATROL: Locked on [{roomName}] {bestPoint.name}";
                return NodeState.SUCCESS;
            }

            context.hunter.currentBTState = "PATROL: No valid targets found (Idle)";
            return NodeState.FAILURE;
        }
    }

    public class MoveToPatrolPoint : HunterTask
    {
        private float switchDistance = 2.0f;

        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            if (context.hunter.currentInterestTarget == null) return NodeState.FAILURE;

            NavMeshAgent agent = context.agent;

            // DIRECT MOVEMENT (No Wiggle)
            // We move to the Vantage Point calculated by AcquirePatrolTarget
            agent.SetDestination(context.hunter.currentNavDestination);
            agent.isStopped = false;

            float dist = Vector3.Distance(agent.transform.position, context.hunter.currentNavDestination);
            if (dist <= switchDistance) return NodeState.SUCCESS;

            return NodeState.RUNNING;
        }
    }

    // =================================================================
    // 3. ACTION BRANCH
    // =================================================================

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

    // --- PEEK NODE (No Debug, No Stop) ---
    public class PerformDoorwayPeek : HunterTask
    {
        private float timer = 0f;
        private float originalSpeed;
        private float originalStoppingDist;

        // Settings
        private float creepSpeed = 0.5f;
        private float maxCreepDist = 1.5f;
        private float peekDuration = 4.5f;

        // State
        private Vector3 creepTargetPos;
        private bool hasValidCreepTarget;

        public PerformDoorwayPeek(HunterBehaviorNodes context) : base(context) { }

        protected override void OnEnter()
        {
            NavMeshAgent agent = context.agent;

            // 1. Capture State
            originalSpeed = agent.speed;
            originalStoppingDist = agent.stoppingDistance;

            // 2. Apply Settings
            agent.speed = creepSpeed;
            agent.stoppingDistance = 0.1f;
            agent.isStopped = false;

            // 3. CALCULATE VALID TARGET (Iterative Fallback)
            Vector3 startPos = context.hunter.currentInterestTarget.position;
            Vector3 forwardDir = context.hunter.currentInterestTarget.forward;

            hasValidCreepTarget = false;
            float[] tryDistances = new float[] { maxCreepDist, 1.0f, 0.5f, 0.2f };

            foreach (float dist in tryDistances)
            {
                // Lifted target check
                Vector3 testPos = startPos + (forwardDir * dist) + (Vector3.up * 0.2f);
                NavMeshHit hit;

                if (NavMesh.SamplePosition(testPos, out hit, 1.0f, NavMesh.AllAreas))
                {
                    // Check path reachability
                    if (context.hunter.IsPathValid(hit.position, out float cost))
                    {
                        agent.SetDestination(hit.position);
                        creepTargetPos = hit.position;
                        hasValidCreepTarget = true;
                        break;
                    }
                }
            }

            if (!hasValidCreepTarget)
            {
                // Fallback: Stand at the patrol point itself
                agent.SetDestination(startPos);
            }

            timer = peekDuration;
            context.hunter.currentBTState = "ACTION: Creeping...";
        }

        protected override NodeState OnUpdate()
        {
            // (Debug Lines Removed for cleaner view)

            timer -= Time.deltaTime;
            if (timer > 0f) return NodeState.RUNNING;
            return NodeState.SUCCESS;
        }

        protected override void OnExit()
        {
            context.agent.speed = originalSpeed;
            context.agent.stoppingDistance = originalStoppingDist;

            if (timer <= 0f && context.hunter.currentInterestTarget != null)
            {
                context.hunter.RecordPatrolVisit(context.hunter.currentInterestTarget);
                context.hunter.currentInterestTarget = null;
            }
        }
    }

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
}
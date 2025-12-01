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
    // 2. PATROL MOVEMENT (Vantage Logic)
    // =================================================================
    public class AcquirePatrolTarget : HunterTask
    {
        public AcquirePatrolTarget(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            // 1. Check if we have a valid target
            if (context.hunter.currentInterestTarget != null)
            {
                // CHECK DISTANCE TO THE CALCULATED VANTAGE POINT (Not the object)
                float dist = Vector3.Distance(context.agent.transform.position, context.hunter.currentNavDestination);

                // If close to the vantage point, success (allow Action to run)
                if (dist <= 1.5f) return NodeState.SUCCESS;

                if (context.hunter.patrolPointData.TryGetValue(context.hunter.currentInterestTarget, out HunterPatrolMemory mem))
                {
                    if (mem.pointType != PointType.Standard) return NodeState.SUCCESS;

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
            Transform bestPoint = context.hunter.GetBestNextPoint(context.agent.transform.position);

            if (bestPoint != null)
            {
                // A. Set the Interest (Data)
                context.hunter.currentInterestTarget = bestPoint;

                // B. Calculate the Vantage Point (Navigation)
                // This is the key "Stalker" update!
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

    public class MoveToPatrolPoint : HunterTask
    {
        private float switchDistance = 1.5f; // Slightly tighter for vantage points

        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context) { }

        protected override NodeState OnUpdate()
        {
            if (context.hunter.currentInterestTarget == null) return NodeState.FAILURE;

            NavMeshAgent agent = context.agent;

            // MOVE TO VANTAGE POINT (Not the object)
            agent.SetDestination(context.hunter.currentNavDestination);
            agent.isStopped = false;

            // Check distance to VANTAGE POINT
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

    // ACTION: Creep & Peek (Event-Driven)
    public class PerformDoorwayPeek : HunterTask
    {
        private float timer = 0f;
        private float originalSpeed;
        private float originalStoppingDist;

        private float creepSpeed = 0.5f;
        private float peekDuration = 4.5f;

        private Vector3 debugTargetPos;
        private bool isValidTarget;

        public PerformDoorwayPeek(HunterBehaviorNodes context) : base(context) { }

        protected override void OnEnter()
        {
            NavMeshAgent agent = context.agent;

            originalSpeed = agent.speed;
            originalStoppingDist = agent.stoppingDistance;

            agent.speed = creepSpeed;
            agent.stoppingDistance = 0.1f;
            agent.isStopped = false;

            // Target Calculation using Partner Point or Lifted Vector
            // (Using the logic we finalized earlier)
            if (context.hunter.currentInterestTarget != null)
            {
                Vector3 startPos = context.hunter.currentInterestTarget.position;
                Vector3 forwardDir = context.hunter.currentInterestTarget.forward;

                // Try to find partner first
                PatrolPoints pointScript = context.hunter.currentInterestTarget.GetComponent<PatrolPoints>();
                Vector3 rawTarget = startPos + (forwardDir * 1.5f); // Default forward

                if (pointScript != null && pointScript.partnerPoint != null)
                {
                    rawTarget = pointScript.partnerPoint.transform.position;
                }

                // Lift & Snap
                NavMeshHit hit;
                if (NavMesh.SamplePosition(rawTarget + Vector3.up * 0.5f, out hit, 2.0f, NavMesh.AllAreas))
                {
                    // Check path validity
                    NavMeshPath path = new NavMeshPath();
                    agent.CalculatePath(hit.position, path);

                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        agent.SetDestination(hit.position);
                        debugTargetPos = hit.position;
                        isValidTarget = true;
                    }
                    else
                    {
                        agent.SetDestination(startPos); // Fallback
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
            // Debug Draw
            Color color = isValidTarget ? Color.green : Color.red;
            if (isValidTarget && !context.agent.hasPath && !context.agent.pathPending) color = Color.yellow;
            Debug.DrawLine(context.agent.transform.position, debugTargetPos, color);

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
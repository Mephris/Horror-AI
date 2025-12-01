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
                // DISTANCE CHECK: We now check distance to the NAV DESTINATION, not the object
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
            Transform bestPoint = context.hunter.GetBestNextPoint(context.agent.transform.position);

            if (bestPoint != null)
            {
                // SET THE INTEREST (Data)
                context.hunter.currentInterestTarget = bestPoint;

                // SET THE DESTINATION (Vantage Logic)
                // "Find a spot 3 meters away from the point so I can see it"
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
        private float switchDistance = 2.0f;
        private NavMeshPath pathContainer;

        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context)
        {
            pathContainer = new NavMeshPath();
        }

        protected override NodeState OnUpdate()
        {
            if (context.hunter.currentInterestTarget == null) return NodeState.FAILURE;

            NavMeshAgent agent = context.agent;

            // FIX: Move to the calculated VANTAGE point, not the object itself
            Vector3 finalTarget = context.hunter.currentNavDestination;

            // CALCULATE DRIFT PATH
            agent.CalculatePath(finalTarget, pathContainer);

            if (pathContainer.status == NavMeshPathStatus.PathComplete || pathContainer.status == NavMeshPathStatus.PathPartial)
            {
                Vector3 carrotPos = GetPointOnPath(pathContainer, context.hunter.pathLookAheadDistance, agent.transform.position);
                Vector3 forward = (carrotPos - agent.transform.position).normalized;
                if (forward == Vector3.zero) forward = agent.transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);

                float wave = Mathf.Sin(Time.time * context.hunter.driftFrequency) * context.hunter.driftAmplitude;
                Vector3 driftTarget = carrotPos + (right * wave);

                NavMeshHit hit;
                if (NavMesh.SamplePosition(driftTarget, out hit, 2.0f, NavMesh.AllAreas))
                    agent.SetDestination(hit.position);
                else
                    agent.SetDestination(carrotPos);
            }
            else
            {
                agent.SetDestination(finalTarget);
            }

            agent.isStopped = false;

            float dist = Vector3.Distance(agent.transform.position, finalTarget);
            if (dist <= switchDistance) return NodeState.SUCCESS;

            return NodeState.RUNNING;
        }

        private Vector3 GetPointOnPath(NavMeshPath path, float distAhead, Vector3 currentPos)
        {
            if (path.corners.Length < 2) return currentPos;
            float distRemaining = distAhead;
            Vector3 previousPoint = currentPos;

            for (int i = 0; i < path.corners.Length; i++)
            {
                Vector3 nextPoint = path.corners[i];
                float distToNext = Vector3.Distance(previousPoint, nextPoint);
                if (distToNext > distRemaining)
                {
                    Vector3 dir = (nextPoint - previousPoint).normalized;
                    return previousPoint + (dir * distRemaining);
                }
                distRemaining -= distToNext;
                previousPoint = nextPoint;
            }
            return path.corners[path.corners.Length - 1];
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


    // =================================================================
    // ACTION: Creep & Peek (Event-Driven Version)
    // =================================================================
    public class PerformDoorwayPeek : HunterTask
    {
        private float timer = 0f;
        private float originalSpeed;
        private float originalStoppingDist;

        // Settings
        private float creepSpeed = 0.5f;
        private float creepDistance = 2.0f; // Increased distance to ensure he crosses threshold
        private float peekDuration = 4.0f;

        // Debug
        private Vector3 debugTargetPos;
        private bool isValidTarget;

        public PerformDoorwayPeek(HunterBehaviorNodes context) : base(context) { }

        protected override void OnEnter()
        {
            NavMeshAgent agent = context.agent;
            originalSpeed = agent.speed;
            originalStoppingDist = agent.stoppingDistance;

            // 1. Slow Down (The Creep)
            agent.speed = creepSpeed;
            agent.stoppingDistance = 0.0f;
            agent.isStopped = false;

            // 2. Calculate Target (Along the Yellow Arrow)
            // We use the PatrolPoint's forward vector which points INTO the linked room
            if (context.hunter.currentInterestTarget != null)
            {
                Vector3 startPos = context.hunter.currentInterestTarget.position;
                Vector3 forwardDir = context.hunter.currentInterestTarget.forward;

                // Lifted Target Fix (+0.5y)
                Vector3 rawTarget = startPos + (forwardDir * creepDistance) + (Vector3.up * 0.5f);

                NavMeshHit hit;
                if (NavMesh.SamplePosition(rawTarget, out hit, 3.0f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                    debugTargetPos = hit.position;
                    isValidTarget = true;
                }
                else
                {
                    // Fallback: Just stand at the threshold
                    agent.SetDestination(startPos);
                    debugTargetPos = startPos;
                    isValidTarget = false;
                }
            }

            timer = peekDuration;
            context.hunter.currentBTState = "ACTION: Creeping & Scanning...";
        }

        protected override NodeState OnUpdate()
        {
            // --- OPTIONAL: FORCE HEAD LOOK ---
            // If you want him to stare at the destination instead of scanning randomly:
            // context.hunter.GetComponent<HunterHeadController>().OverrideLookTarget(debugTargetPos);

            // Visuals
            Color color = isValidTarget ? Color.green : Color.red;
            if (isValidTarget && !context.agent.hasPath) color = Color.yellow;
            Debug.DrawLine(context.agent.transform.position, debugTargetPos, color);

            timer -= Time.deltaTime;

            if (timer > 0f) return NodeState.RUNNING;

            return NodeState.SUCCESS;
        }

        protected override void OnExit()
        {
            // Restore
            context.agent.speed = originalSpeed;
            context.agent.stoppingDistance = originalStoppingDist;

            // Mark Visited
            if (context.hunter.currentInterestTarget != null)
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
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
    // 1. CHASE BRANCH (Priority High)
    // =================================================================

    public class IsPlayerSeen : HunterCondition
    {
        public IsPlayerSeen(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            if (context.hunter.timeSinceLastSeen == 0.0f)
            {
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            if (context.hunter.isChasingPlayer &&
                context.hunter.timeSinceLastSeen < context.hunter.chaseInvestigationTime)
            {
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            context.hunter.isChasingPlayer = false;
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    public class ChasePlayer : HunterTask
    {
        private float acceptableDistance = 1.0f;
        private float velocityStopThreshold = 0.1f;

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

            if (isPathNotPending && isCloseEnough && isStoppedMoving)
            {
                agent.isStopped = true;
                context.hunter.currentBTState = "CHASING: Arrived at Last Seen. Investigating...";
                nodeState = NodeState.RUNNING; // Hold state until timer expires in HunterAI
                return nodeState;
            }
            else
            {
                agent.isStopped = false;
                context.hunter.currentBTState = "CHASING: Moving to Last Seen Position";
            }

            nodeState = NodeState.RUNNING;
            return nodeState;
        }
    }

    // =================================================================
    // 2. PATROL BRANCH (The Reactive Loop)
    // =================================================================

    // NEW NODE: Replaces the "Planner". 
    // It simply finds the best target available right now.
    public class AcquirePatrolTarget : HunterTask
    {
        public AcquirePatrolTarget(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            // 1. Check if we already have a valid target we are moving towards
            if (context.hunter.currentPatrolTarget != null)
            {
                // If we are far enough away, keep it.
                float dist = Vector3.Distance(context.agent.transform.position, context.hunter.currentPatrolTarget.position);
                if (dist > 3.0f)
                {
                    nodeState = NodeState.SUCCESS;
                    return nodeState;
                }

                // If we are close (within 3m), mark visited and force a re-pick.
                context.hunter.RecordPatrolVisit(context.hunter.currentPatrolTarget);
                context.hunter.currentPatrolTarget = null;
            }

            // 2. Find the BEST next target
            // This function (in HunterAI.cs) now handles Heat, Distance, Doors, and Momentum.
            Transform bestPoint = context.hunter.GetBestNextPoint(context.agent.transform.position);

            if (bestPoint != null)
            {
                context.hunter.currentPatrolTarget = bestPoint;

                // Debug Name
                string roomName = "Hallway";
                if (context.hunter.patrolPointData.TryGetValue(bestPoint, out HunterPatrolMemory mem) && mem.parentRoom != null)
                {
                    roomName = mem.parentRoom.roomName;
                }

                context.hunter.currentBTState = $"PATROL: Locked on [{roomName}] {bestPoint.name}";
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // 3. Failsafe
            context.hunter.currentBTState = "PATROL: No valid targets found (Idle)";
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    // UPDATED NODE: Simple continuous movement
    public class MoveToPatrolPoint : HunterTask
    {
        private float switchDistance = 2.0f; // Don't stop, switch early

        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            if (context.hunter.currentPatrolTarget == null) return NodeState.FAILURE;

            NavMeshAgent agent = context.agent;
            agent.SetDestination(context.hunter.currentPatrolTarget.position);
            agent.isStopped = false;

            float dist = agent.pathPending ? Vector3.Distance(agent.transform.position, context.hunter.currentPatrolTarget.position) : agent.remainingDistance;

            // --- CONTINUOUS FLOW LOGIC ---
            if (dist <= switchDistance && !agent.pathPending)
            {
                // We are close enough. Return SUCCESS.
                // This causes the Sequence to finish and restart immediately.
                // The 'AcquirePatrolTarget' node will see we are close, mark it visited, and pick a NEW target.
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

            // Keep moving
            // context.hunter.currentBTState = $"MOVING: {dist:F1}m to target"; 
            nodeState = NodeState.RUNNING;
            return nodeState;
        }
    }
}
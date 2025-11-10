using UnityEngine;
using UnityEngine.AI;
using static Node;

// Note: You must ensure this script has access to the Hunter_Basic component!

public class HunterBehaviorNodes
{
    private Hunter_Basic hunter;
    private NavMeshAgent agent;

    public HunterBehaviorNodes(Hunter_Basic hunterComponent, NavMeshAgent navAgent)
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
    // 1. CONDITION: Is Player Visible? (Checks the public targetPos)
    // =================================================================
    public class IsPlayerSeen : HunterCondition
    {
        public IsPlayerSeen(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            // FIX 1: Now works because targetPos is public in Hunter_Basic
            if (context.hunter.targetPos != null)
            {
                nodeState = NodeState.SUCCESS;
                return nodeState;
            }

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
                // FIX 2: Now works because GetRandomWanderPoint is public in Hunter_Basic
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
    public class IsAtDestination : HunterCondition
    {
        private float acceptableDistance = 0.5f;

        public IsAtDestination(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;

            if (!agent.pathPending)
            {
                if (agent.remainingDistance <= acceptableDistance)
                {
                    // FIX 3: Now works because currentPatrolTarget is public in Hunter_Basic
                    if (context.hunter.currentPatrolTarget != null)
                    {
                        // In the future, you'll call a method here to mark the point as visited
                        // context.hunter.ArrivedAtPatrolPoint(); 
                    }

                    nodeState = NodeState.SUCCESS;
                    return nodeState;
                }
            }

            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    // =================================================================
    // 4. TASK: Move to Patrol Point (Finds a new point and moves there)
    // =================================================================
    public class MoveToPatrolPoint : HunterTask
    {
        public MoveToPatrolPoint(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;

            // Step 1: Check if we are already moving to a known patrol point
            if (agent.hasPath && context.hunter.currentPatrolTarget != null)
            {
                // If the current path is valid, keep running the task.
                nodeState = NodeState.RUNNING;
                return nodeState;
            }

            // Step 2: If not moving or target is cleared, find the next best target.
            Transform bestPatrolPoint = context.hunter.GetBestPatrolPoint();

            if (bestPatrolPoint != null)
            {
                // Update the Hunter's current target variable
                context.hunter.currentPatrolTarget = bestPatrolPoint;

                // Set the new destination
                agent.SetDestination(bestPatrolPoint.position);

                nodeState = NodeState.RUNNING;
                return nodeState;
            }

            // If no valid patrol point could be found (e.g., all points checked)
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    // =================================================================
    // 5. TASK: Chase Player (High Priority Action)
    // =================================================================
    public class ChasePlayer : HunterTask
    {
        // How long the Hunter will investigate the last known location after losing sight
        private float investigationTimeLimit = 5.0f;
        private float chaseStartTime = 0f; // Stores when the chase started (or target was set)
        private float acceptableDistance = 0.5f; // How close is "close enough"

        public ChasePlayer(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;
            Transform targetTransform = context.hunter.targetPos;

            // --- Step 1: Check if we have a valid target to chase ---
            if (targetTransform == null)
            {
                // This should ideally never happen because IsPlayerSeen checks targetPos,
                // but it's a safety check.
                return NodeState.FAILURE;
            }

            // --- Step 2: Set or Maintain Destination ---
            // Always set the destination to the current targetPos's location. 
            // This handles cases where OnSeePlayer updates the position every 0.5s.
            agent.SetDestination(targetTransform.position);


            // --- Step 3: Check for Completion/Failure (Loss of Target) ---

            // Check 3A: If we are close to the target location
            bool hasArrived = agent.remainingDistance <= acceptableDistance && !agent.pathPending;

            // The IsPlayerSeen condition in the BT (Selector) will check for actual line of sight.
            // We need an internal check to see if we're done with the chase command.

            // If the Hunter has arrived at the last known location...
            if (hasArrived)
            {
                // ... AND the Hunter hasn't seen the player for the investigation limit...
                // You will need to add a timer mechanism to Hunter_Basic to track "time since last seen"

                // Temporary Logic: For now, if we arrive at the static targetPos and can't see the player, we fail.
                // This assumes IsPlayerSeen fails when the player is out of sight.
                nodeState = NodeState.FAILURE;
                return nodeState;
            }

            // --- Step 4: Chase is Running ---
            nodeState = NodeState.RUNNING;
            return nodeState;
        }
    }
}
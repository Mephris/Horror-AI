// --- HunterBehaviorNodes.cs ---

using UnityEngine;
using UnityEngine.AI;
using static Node; // Allows you to use NodeState directly (SUCCESS, FAILURE, RUNNING)

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
    // BASE CLASSES (Moved here from Hunter_Basic for organization)
    // =================================================================

    // Task Node: Represents an action that takes time (e.g., movement)
    public abstract class HunterTask : Node
    {
        protected HunterBehaviorNodes context;

        public HunterTask(HunterBehaviorNodes context)
        {
            this.context = context;
        }
    }

    // Condition Node: Represents a simple check (e.g., IsPlayerSeen)
    public abstract class HunterCondition : Node
    {
        protected HunterBehaviorNodes context;

        public HunterCondition(HunterBehaviorNodes context)
        {
            this.context = context;
        }
    }

    // =================================================================
    // 1. CONDITION: Is Player Visible? (For the CHASE Branch)
    // =================================================================
    public class IsPlayerSeen : HunterCondition
    {
        public IsPlayerSeen(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            // Assuming the FieldOfView script sets a public 'canSeeTarget' or a similar flag
            // For now, we'll rely on the Hunter's current data that is set via the Actions.
            // If the Hunter has a target from the FOV script, or a recent last-seen location, we SUCCEED.

            // Note: You might need to adjust this to check a boolean property on the Hunter.
            // For simplicity, let's assume 'hunter.targetPos' is only set during a chase.
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
    // 2. TASK: Wander/Roam Locally (The goal of this implementation phase)
    // =================================================================
    public class WanderLocally : HunterTask
    {
        private float wanderRange = 5f; // Local override for how far to wander
        private float acceptableDistance = 0.5f; // How close is "close enough"

        public WanderLocally(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;

            // Step 1: Check if we have arrived at the previous wander destination
            if (agent.remainingDistance <= acceptableDistance && !agent.pathPending)
            {
                // We arrived or failed, so pick a new random point
                Vector3 newWanderPoint = context.hunter.GetRandomWanderPoint(agent.transform.position, wanderRange);

                if (newWanderPoint != Vector3.zero)
                {
                    agent.SetDestination(newWanderPoint);
                    // Since we just started a move, the node is RUNNING
                    nodeState = NodeState.RUNNING;
                    return nodeState;
                }
                else
                {
                    // Failed to find a spot (stuck), so the task fails, and the BT moves on.
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                }
            }

            // Step 2: If we are already moving, the task is still RUNNING
            if (agent.hasPath || agent.pathPending)
            {
                nodeState = NodeState.RUNNING;
                return nodeState;
            }

            // If we are stopped and haven't set a path, it's a failure (or we need to choose a new point)
            nodeState = NodeState.FAILURE;
            return nodeState;
        }
    }

    // =================================================================
    // 3. CONDITION: Is Agent At Patrol Point Destination?
    // =================================================================
    public class IsAtDestination : HunterCondition
    {
        private float acceptableDistance = 0.5f; // How close is "close enough" to the target

        public IsAtDestination(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            NavMeshAgent agent = context.agent;

            if (!agent.pathPending)
            {
                if (agent.remainingDistance <= acceptableDistance)
                {
                    // Action upon arrival (e.g., mark the point as visited)
                    if (context.hunter.currentPatrolTarget != null)
                    {
                        // Assuming you add an ArrivedAtPatrolPoint method to Hunter_Basic
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
}
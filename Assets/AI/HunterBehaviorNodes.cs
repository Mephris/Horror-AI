using UnityEngine;
using UnityEngine.AI;
// This 'using static' works because the 'Node' class is defined in BehaviorTree.cs
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

    // --- In HunterBehaviorNodes.cs (Add this class) ---

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
}
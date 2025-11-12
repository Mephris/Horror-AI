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
                    if (context.hunter.currentPatrolTarget != null)
                    {
                        // The Agent has arrived. Inform the Hunter_Basic component.
                        context.hunter.ArrivedAtPatrolPoint();
                    }

                    // Returning SUCCESS allows the parent Sequence/Selector to move to the next step,
                    // which is usually a Wait/Pause node before the next patrol begins.
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
                context.hunter.currentBTState = $"PATROL: Moving to {bestPatrolPoint.gameObject.name}";

                // Set the new destination
                agent.SetDestination(bestPatrolPoint.position);

                nodeState = NodeState.RUNNING;
                return nodeState;
            }

            // ... (existing logic returns FAILURE if no patrol point is found)
            context.hunter.currentBTState = "PATROL: No Points Found / Stuck";

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
                // for the 7-second investigation timer (managed in Hunter_Basic.cs) to expire.
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

    // --- HunterBehaviorNodes.cs ---

    // Add this new class to your HunterBehaviorNodes.cs file:
    // This node handles the time the Hunter spends looking around the point.
    public class InvestigatePatrolPoint : HunterTask
    {
        private bool hasStartedInvestigation = false;

        public InvestigatePatrolPoint(HunterBehaviorNodes context) : base(context) { }

        public override NodeState Evaluate()
        {
            // 1. Check if we have a valid target to investigate.
            if (context.hunter.currentPatrolTarget == null)
            {
                hasStartedInvestigation = false;
                context.hunter.isInvestigating = false;
                return NodeState.FAILURE;
            }

            // 2. First evaluation: Start the investigation timer.
            if (!hasStartedInvestigation)
            {
                context.hunter.StartInvestigation(context.hunter.currentPatrolTarget);
                hasStartedInvestigation = true;
                context.hunter.currentBTState = $"PATROL: Investigating (Duration: {context.hunter.investigationDuration:F2}s)";
                nodeState = NodeState.RUNNING;
                return nodeState;
            }

            // 3. Subsequent evaluations: Update the timer.
            NodeState timerState = context.hunter.UpdateInvestigationTimer();

            if (timerState == NodeState.SUCCESS)
            {
                // The timer expired, investigation is complete.
                hasStartedInvestigation = false;
                context.hunter.currentBTState = "PATROL: Investigation Complete";
            }
            else if (timerState == NodeState.RUNNING)
            {
                // Still waiting.
                context.hunter.currentBTState = $"PATROL: Investigating ({context.hunter.investigationDuration - context.hunter.investigationTimeElapsed:F2}s left)";
            }

            nodeState = timerState;
            return nodeState;
        }
    }

}
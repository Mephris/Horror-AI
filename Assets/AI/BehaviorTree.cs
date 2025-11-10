// --- BehaviorTree.cs ---
using System.Collections.Generic;

// --- Base Node Class ---
public abstract class Node
{
    public enum NodeState { RUNNING, SUCCESS, FAILURE }
    protected NodeState nodeState;

    public NodeState GetNodeState() { return nodeState; }

    // The core function that runs the logic of this node
    public abstract NodeState Evaluate();
}

// --- Composite Node Class (Base for branches like Selector/Sequence) ---
public abstract class CompositeNode : Node
{
    protected List<Node> children = new List<Node>();

    public CompositeNode(List<Node> children)
    {
        this.children = children;
    }
}

// --- Selector Node (Priority Check / OR logic) ---
public class Selector : CompositeNode
{
    public Selector(List<Node> children) : base(children) { }

    public override NodeState Evaluate()
    {
        foreach (Node node in children)
        {
            switch (node.Evaluate())
            {
                case NodeState.FAILURE:
                    continue;
                case NodeState.SUCCESS:
                    nodeState = NodeState.SUCCESS;
                    return nodeState;
                case NodeState.RUNNING:
                    nodeState = NodeState.RUNNING;
                    return nodeState;
            }
        }
        nodeState = NodeState.FAILURE;
        return nodeState;
    }
}

// --- Sequence Node (Procedure Check / AND logic) ---
public class Sequence : CompositeNode
{
    public Sequence(List<Node> children) : base(children) { }

    public override NodeState Evaluate()
    {
        bool anyChildIsRunning = false;

        // This index tracks where the sequence left off, which is important
        // if you implement "memory" in the sequence, but for simplicity now, 
        // we check from the start.

        foreach (Node node in children)
        {
            switch (node.Evaluate())
            {
                case NodeState.FAILURE:
                    nodeState = NodeState.FAILURE;
                    return nodeState;
                case NodeState.SUCCESS:
                    continue;
                case NodeState.RUNNING:
                    anyChildIsRunning = true;
                    // If one task is running, the sequence is RUNNING, but we check subsequent tasks
                    // if they are non-blocking. For simplicity, break if a task is running.
                    nodeState = NodeState.RUNNING;
                    return nodeState;
            }
        }

        nodeState = anyChildIsRunning ? NodeState.RUNNING : NodeState.SUCCESS;
        return nodeState;
    }
}
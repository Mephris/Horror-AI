using System.Collections.Generic;

// =================================================================
// --- BASE NODE (Stateful) ---
// =================================================================
public abstract class Node
{
    public enum NodeState { RUNNING, SUCCESS, FAILURE }
    protected NodeState nodeState;

    public string customName = "";
    public string description = "";

    // Tracks if this node is currently active.
    // This prevents "OnEnter" from running every frame.
    protected bool started = false;

    public NodeState GetNodeState() { return nodeState; }

    // The Public API called by parents. 
    // DO NOT OVERRIDE THIS. Override OnUpdate instead.
    public NodeState Evaluate()
    {
        // 1. Life Cycle: Enter (Runs once when starting)
        if (!started)
        {
            OnEnter();
            started = true;
        }

        // 2. Life Cycle: Update (Runs every frame)
        nodeState = OnUpdate();

        // 3. Life Cycle: Exit (Runs once when finished)
        if (nodeState != NodeState.RUNNING)
        {
            OnExit();
            started = false;
        }

        return nodeState;
    }

    // Force Reset (called by Parent when interrupting/switching branches)
    public void Abort()
    {
        if (started)
        {
            OnExit();
            started = false;
            nodeState = NodeState.FAILURE; // Reset state to avoid visual confusion
        }
    }

    // --- VIRTUAL METHODS (Override these in your Tasks) ---

    // Called once when the node starts running. Setup variables here.
    protected virtual void OnEnter() { }

    // Called every frame. Return RUNNING, SUCCESS, or FAILURE.
    protected abstract NodeState OnUpdate();

    // Called once when the node finishes OR is forced to stop. Cleanup here.
    protected virtual void OnExit() { }
}

// =================================================================
// --- COMPOSITE NODE ---
// =================================================================
public abstract class CompositeNode : Node
{
    protected List<Node> children = new List<Node>();
    public List<Node> Children => children;

    public CompositeNode(List<Node> children)
    {
        this.children = children;
    }
}

// =================================================================
// --- SELECTOR ("OR" Logic) ---
// =================================================================
public class Selector : CompositeNode
{
    public Selector(List<Node> children) : base(children) { }

    public Selector(string name, string desc, List<Node> children) : base(children)
    {
        this.customName = name;
        this.description = desc;
    }

    protected override NodeState OnUpdate()
    {
        foreach (Node node in children)
        {
            switch (node.Evaluate())
            {
                case NodeState.FAILURE:
                    continue; // Try next child
                case NodeState.SUCCESS:
                    return NodeState.SUCCESS;
                case NodeState.RUNNING:
                    return NodeState.RUNNING;
            }
        }
        return NodeState.FAILURE; // All children failed
    }

    // CRITICAL: If the Selector stops running, ensure children stop too.
    protected override void OnExit()
    {
        foreach (Node node in children)
        {
            node.Abort();
        }
    }
}

// =================================================================
// --- SEQUENCE ("AND" Logic) ---
// =================================================================
public class Sequence : CompositeNode
{
    public Sequence(List<Node> children) : base(children) { }

    public Sequence(string name, string desc, List<Node> children) : base(children)
    {
        this.customName = name;
        this.description = desc;
    }

    protected override NodeState OnUpdate()
    {
        bool anyChildIsRunning = false;

        foreach (Node node in children)
        {
            switch (node.Evaluate())
            {
                case NodeState.FAILURE:
                    return NodeState.FAILURE; // Stop sequence
                case NodeState.SUCCESS:
                    continue; // Run next child
                case NodeState.RUNNING:
                    anyChildIsRunning = true;
                    return NodeState.RUNNING;
            }
        }

        // If we get here, all children succeeded?
        // Logic check: If a child returns running, we return running immediately above.
        // So if we finish the loop, it means all returned SUCCESS.
        return anyChildIsRunning ? NodeState.RUNNING : NodeState.SUCCESS;
    }

    // CRITICAL: If the Sequence stops running, ensure children stop too.
    protected override void OnExit()
    {
        foreach (Node node in children)
        {
            node.Abort();
        }
    }
}
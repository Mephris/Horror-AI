using UnityEngine;

// 1. The Main Verb (What to do?)
public enum JobType
{
    Move,       // Locomotion
    Action,     // Animation (Peek, Search, Taunt)
    Wait        // Idling
}

// 2. The Strategy (How to do it?) - "The Last of Us" Style Intent
public enum MoveStrategy
{
    Direct,     // Go exactly to target (e.g., entering a trigger)
    Vantage,    // Find a tactical viewing spot (Stalking logic)
    Creep       // Slow, silent approach (Future use)
}

[System.Serializable]
public class HunterJob
{
    [Header("The Task")]
    public JobType jobType;
    public MoveStrategy moveStrategy; // Only used if Type == Move

    [Header("The Context")]
    // The "Anchor" - The Director/Planner points at this object.
    // The Executor will figure out the actual floor position later.
    public Transform targetInterest;

    // Optional: If we want to go to a blank point in space (no object)
    public Vector3? fallbackPosition;

    [Header("Parameters")]
    public float duration; // For Wait/Action
    public float speed;    // 0.5 = Walk, 1.0 = Run

    // --- FACTORY HELPERS (The "Nanojob" Recipes) ---

    // Recipe 1: Stalk an Object (Vantage Logic)
    public static HunterJob CreateStalk(Transform target)
    {
        return new HunterJob
        {
            jobType = JobType.Move,
            moveStrategy = MoveStrategy.Vantage,
            targetInterest = target,
            speed = 0.5f // Creepy walk
        };
    }

    // Recipe 2: Go To Location (Direct Logic)
    public static HunterJob CreateMoveTo(Vector3 pos)
    {
        return new HunterJob
        {
            jobType = JobType.Move,
            moveStrategy = MoveStrategy.Direct,
            fallbackPosition = pos,
            speed = 0.6f
        };
    }

    // Recipe 3: Perform Action (Peek/Search)
    public static HunterJob CreateAction(JobType type, Transform target, float time)
    {
        return new HunterJob
        {
            jobType = type,
            targetInterest = target,
            duration = time
        };
    }
}
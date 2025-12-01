using UnityEngine;

// Defines the atomic actions the Hunter can perform
public enum HunterJobType
{
    MoveTo,     // Walk to a specific floor coordinate (Vantage Point)
    Peek,       // Stop and look at an object (Target)
    Interact,   // Play an animation at an object (Open/Kick)
    Wait        // Idle for a duration
}

[System.Serializable]
public class HunterJob
{
    [Header("Instruction")]
    public HunterJobType jobType;
    public float duration;           // How long to Peek/Wait

    [Header("Data")]
    public Vector3 vantagePosition;  // WHERE to stand (The calculated floor spot)
    public Transform targetInterest; // WHAT to look at (The Door/Table)
    public float moveSpeed = 3.5f;   // Speed override for this specific job

    // --- CONSTRUCTORS (Helpers for easy creation) ---

    // 1. Movement Job (Go to the Vantage Point)
    public static HunterJob CreateMove(Vector3 destination, float speed = 3.5f)
    {
        return new HunterJob
        {
            jobType = HunterJobType.MoveTo,
            vantagePosition = destination,
            moveSpeed = speed,
            targetInterest = null
        };
    }

    // 2. Peek Job (Stand still and look at the Object)
    public static HunterJob CreatePeek(Transform targetToLookAt, float time = 4.0f)
    {
        return new HunterJob
        {
            jobType = HunterJobType.Peek,
            targetInterest = targetToLookAt,
            duration = time
        };
    }

    // 3. Wait Job (Just pause)
    public static HunterJob CreateWait(float time)
    {
        return new HunterJob
        {
            jobType = HunterJobType.Wait,
            duration = time
        };
    }
}
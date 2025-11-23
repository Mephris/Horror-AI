using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 1. Define the types of points available
// This Enum needs to be visible to HunterAI.cs, so we define it outside the class
public enum PointType
{
    Standard,   // Normal floor spot
    Doorway,    // A spot looking into a room (High Priority)
    HidingSpot, // A spot near a locker/table (Paranoia Priority)
    Vent        // For future use
}

public class PatrolPoints : MonoBehaviour
{
    [Header("Smart Data")]
    public PointType pointType = PointType.Standard;
    public Room linkedRoom;

    // --- NEW: Direct Reference to Live Data ---
    // The HunterAI will plug this in during Start().
    // We don't Serialize it because it's runtime-only data (Circular ref risk in Editor).
    [System.NonSerialized]
    public HunterPatrolMemory runtimeMemory;

    private void OnDrawGizmos()
    {
        // 1. Default to "Cold" (Green)
        float probability = 0f;

        // 2. If the game is running and Hunter has given us data, read it directly!
        if (runtimeMemory != null)
        {
            probability = runtimeMemory.playerProbability;
        }

        // 3. Draw Gizmo
        Gizmos.color = Color.Lerp(Color.green, Color.red, probability);
        float scale = 0.2f + (probability * 0.15f);
        Gizmos.DrawCube(transform.position, Vector3.one * scale);

        // Optional: Draw Doorway direction
        if (pointType == PointType.Doorway)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, transform.forward * 0.5f);
        }
    }
}
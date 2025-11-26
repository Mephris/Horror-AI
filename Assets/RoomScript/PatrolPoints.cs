using UnityEditor;
using UnityEngine;

// 1. Define the types so they are visible to HunterAI and HunterBehaviorNodes
public enum PointType
{
    Standard,   // Normal walking path
    Doorway,    // Triggers "Peek" animation
    HidingSpot, // Triggers "Crouch/Search" animation
    Vent        // Future use
}

public class PatrolPoints : MonoBehaviour
{
    [Header("Smart Data")]
    [Tooltip("What is this point? Affects Hunter behavior.")]
    public PointType pointType = PointType.Standard;

    [Tooltip("If this is a Doorway, drag the Room script it looks INTO here.")]
    public Room linkedRoom;

    [Tooltip("Optional: Drag the Room this point is physically standing inside. Overrides Hierarchy.")]
    public Room manualRoomOwner;

    [Tooltip("The patrol point on the other side of this object (for Doors).")]
    public PatrolPoints partnerPoint;

    // --- RUNTIME DATA ---
    // Injected by HunterAI.Start(). 
    // This allows the point to know its own heat without expensive lookups.
    [System.NonSerialized]
    public HunterPatrolMemory runtimeMemory;

    private void OnDrawGizmos()
    {
        // 1. Default Heat
        float probability = 0f;

        // 2. Read live data
        if (runtimeMemory != null)
        {
            if (pointType == PointType.Doorway && runtimeMemory.linkedRoomInfo != null)
            {
                probability = runtimeMemory.linkedRoomInfo.generalCuriosity;
            }
            else
            {
                probability = runtimeMemory.playerProbability;
            }
        }

        // 3. Draw Heat Cube
        Gizmos.color = Color.Lerp(Color.green, Color.red, probability);
        float scale = 0.2f + (probability * 0.15f);
        Gizmos.DrawCube(transform.position, Vector3.one * scale);

        // 4. Draw The "Dart" (Direction)
        // We only draw this for points where orientation matters (Doors, Hiding Spots)
        if (pointType != PointType.Standard)
        {
#if UNITY_EDITOR
            // Use Handles for a solid, professional look
            Handles.color = new Color(1f, 0.92f, 0.016f, 1f); // Bright Yellow

            Vector3 startPos = transform.position + Vector3.up * 1.5f; // Eye level
            Vector3 direction = transform.forward;
            float length = 0.8f;
            float headSize = 0.3f;

            // Draw the shaft (Thick Line)
            Handles.DrawLine(startPos, startPos + direction * length, 2.0f);

            // Draw the tip (Solid Cone)
            Vector3 tipPos = startPos + direction * length;
            Handles.ConeHandleCap(0, tipPos, Quaternion.LookRotation(direction), headSize, EventType.Repaint);
#else
            // Fallback for very rare runtime debug cases (simple lines)
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * 1.5f, transform.forward);
#endif
        }
    }
}
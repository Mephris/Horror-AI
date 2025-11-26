using UnityEngine;
#if UNITY_EDITOR
using UnityEditor; // Required for drawing text labels
#endif

[ExecuteInEditMode]
public class DoorConnectionHandler : MonoBehaviour
{
    [Header("1. Assign Rooms Here")]
    public Room roomA; // e.g., Kitchen
    public Room roomB; // e.g., Hallway

    [Header("2. Assign Child Points (Once in Prefab)")]
    [Tooltip("The point physically sitting inside Room A")]
    public PatrolPoints pointInRoomA;

    [Tooltip("The point physically sitting inside Room B")]
    public PatrolPoints pointInRoomB;

    private void OnValidate()
    {
        UpdateChildren();
    }

    [ContextMenu("Force Update")]
    public void UpdateChildren()
    {
        // --- LOGIC FOR POINT A ---
        if (pointInRoomA != null)
        {
            pointInRoomA.manualRoomOwner = roomA;
            pointInRoomA.linkedRoom = roomB;
            pointInRoomA.pointType = PointType.Doorway;
            pointInRoomA.name = "Point_Side_A";
        }

        // --- LOGIC FOR POINT B ---
        if (pointInRoomB != null)
        {
            pointInRoomB.manualRoomOwner = roomB;
            pointInRoomB.linkedRoom = roomA;
            pointInRoomB.pointType = PointType.Doorway;
            pointInRoomB.name = "Point_Side_B";
        }
        // Wire the partners of the same doorway. 
        if (pointInRoomA != null && pointInRoomB != null)
        {
            // Tell A that B is its partner
            pointInRoomA.partnerPoint = pointInRoomB;

            // Tell B that A is its partner
            pointInRoomB.partnerPoint = pointInRoomA;
        }
    }

    // --- VISUAL HELPERS ---
    private void OnDrawGizmos()
    {
        // 1. Draw Lines connecting rooms
        if (roomA != null && roomB != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(roomA.transform.position, transform.position);
            Gizmos.DrawLine(roomB.transform.position, transform.position);
        }

#if UNITY_EDITOR
        // 2. Draw Labels "A" and "B"
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 20;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;

        if (pointInRoomA != null)
        {
            Handles.Label(pointInRoomA.transform.position + Vector3.up * 1.0f, "A", style);
        }

        if (pointInRoomB != null)
        {
            Handles.Label(pointInRoomB.transform.position + Vector3.up * 1.0f, "B", style);
        }
#endif
    }
}
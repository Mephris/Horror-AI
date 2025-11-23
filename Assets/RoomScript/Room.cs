using System.Collections.Generic;
using UnityEngine;

public class Room : MonoBehaviour
{
    public PatrolPoints[] patrolPoint;

    [Header("Navigation Graph")]
    [Tooltip("Drag and drop every Room that is directly reachable from this one.")]
    public List<Room> neighbors = new List<Room>();

    [SerializeField] public bool isNearbyPlayer; // You can keep this serialized

    private void Awake()
    {
        patrolPoint = GetComponentsInChildren<PatrolPoints>();

        FindNeighborsFromDoors();
    }

    // This adds a right-click option in the Unity Inspector
    [ContextMenu("Auto-Find Neighbors from Doors")]
    public void FindNeighborsFromDoors()
    {
        // 1. Clear current list to avoid duplicates
        neighbors.Clear();

        // 2. Check all patrol points in this room
        // (Assuming you have a list/array called patrolPoint)
        foreach (var point in patrolPoint)
        {
            // 3. If it's a Doorway AND has a link
            if (point.pointType == PointType.Doorway && point.linkedRoom != null)
            {
                // 4. Add the linked room if it's not already in the list
                if (!neighbors.Contains(point.linkedRoom))
                {
                    neighbors.Add(point.linkedRoom);
                }

                // Optional: Auto-link back? (Make the Hallway know about the Kitchen too)
                if (!point.linkedRoom.neighbors.Contains(this))
                {
                    point.linkedRoom.neighbors.Add(this);
                }
            }
        }

        Debug.Log($"{name}: Found {neighbors.Count} neighbors via Doorways.");
    }


    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.3f);
    }
}
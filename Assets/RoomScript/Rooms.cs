using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
// Removed: using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters; (Non-runtime dependency)

public class Rooms : MonoBehaviour
{

    [SerializeField] private Room[] rooms;
    private GameObject player;


    private void Awake()
    {
        // Ensure rooms is initialized correctly
        rooms = GetComponentsInChildren<Room>();
        player = GameObject.FindGameObjectWithTag("Player");
        // Ensure the player is found
        if (player == null)
        {
            Debug.LogError("Rooms.cs: Could not find GameObject with tag 'Player'.");
        }
    }

    private void Update()
    {
        UpdateRoomProximity();
    }

    private void UpdateRoomProximity()
    {
        if (player == null || rooms == null) return;

        // Get player's position
        Vector3 playerPosition = player.transform.position;

        // Iterate through each room
        foreach (Room room in rooms)
        {
            // Create a path from player to room
            NavMeshPath path = new NavMeshPath();
            // Note: NavMesh.CalculatePath can fail. We proceed only if successful.
            bool pathFound = NavMesh.CalculatePath(playerPosition, room.transform.position, NavMesh.AllAreas, path);

            // Calculate the cost of the created path
            float pathCost = pathFound ? CalculatePathCost(path) : float.MaxValue;

            // If the path cost is less than or equal to 40, set isNearby to true
            room.isNearbyPlayer = (pathCost <= 40f);
        }
    }

    public float CalculatePathCost(NavMeshPath path)
    {
        float cost = 0f;

        // Sum up the cost of each segment in the path
        for (int i = 0; i < path.corners.Length - 1; i++)
        {
            cost += Vector3.Distance(path.corners[i], path.corners[i + 1]);
        }

        // Return MaxValue if the path is incomplete or invalid
        if (path.status != NavMeshPathStatus.PathComplete)
        {
            return float.MaxValue;
        }

        return cost;
    }

    /// <summary>
    /// Finds the Room component that is closest to the player, measured by NavMesh path cost.
    /// This method resolves the 'ClosestRoomComponent' compilation error.
    /// </summary>
    public Room ClosestRoomComponent()
    {
        // Use the helper method to find the room with the lowest path cost (closest)
        return GetRoomByPathCostGoal(false);
    }

    // A necessary helper for the original PosFarFromPlayer logic
    public Room MostCostMovement(Vector3 startPos)
    {
        return GetRoomByPathCostGoal(true);
    }

    // Helper method to find the room component based on cost (Min-False or Max-True)
    private Room GetRoomByPathCostGoal(bool findMaxCost)
    {
        if (player == null || rooms == null || rooms.Length == 0) return null;

        Room targetRoom = null;
        float targetCost = findMaxCost ? float.MinValue : float.MaxValue;
        Vector3 playerPosition = player.transform.position;

        foreach (Room room in rooms)
        {
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(playerPosition, room.transform.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                float cost = CalculatePathCost(path);

                if (findMaxCost)
                {
                    if (cost > targetCost)
                    {
                        targetCost = cost;
                        targetRoom = room;
                    }
                }
                else // Find Min Cost (Closest)
                {
                    if (cost < targetCost)
                    {
                        targetCost = cost;
                        targetRoom = room;
                    }
                }
            }
        }

        return targetRoom;
    }

    // The existing methods are re-implemented here to use the new robust logic



    public Vector3 PosFarFromPlayer()
    {
        Room targetRoom = GetRoomByPathCostGoal(true);
        NavMeshPath path = new NavMeshPath();

        // This pattern relies on the Director component being present and having a public hunterAgent.
        // It's generally safer to pass the agent/position, but we maintain the existing FindObjectOfType structure.
        NavMeshAgent hunterAgent = FindObjectOfType<Director>()?.hunterAgent;

        if (hunterAgent != null && targetRoom != null && hunterAgent.CalculatePath(targetRoom.transform.position, path))
        {
            if (path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 1)
            {
                // The second-to-last corner is the new endpoint
                return path.corners[path.corners.Length - 2];
            }
            else if (path.status == NavMeshPathStatus.PathComplete && path.corners.Length == 1)
            {
                return path.corners[0];
            }
        }

        // Fallback or error case
        return targetRoom != null ? targetRoom.transform.position : Vector3.zero;
    }

    public Vector3 FurthestRoom()
    {
        Room furthestRoom = GetRoomByPathCostGoal(true);
        return furthestRoom != null ? furthestRoom.transform.position : Vector3.zero;
    }

    public Vector3 ClosestRoom()
    {
        Room closestRoom = GetRoomByPathCostGoal(false);
        return closestRoom != null ? closestRoom.transform.position : Vector3.zero;
    }
}
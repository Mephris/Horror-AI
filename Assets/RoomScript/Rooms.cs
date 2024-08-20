using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters;

public class Rooms : MonoBehaviour
{

    private Room[] rooms;
    private GameObject player;


    private void Awake()
    {
        rooms = GetComponentsInChildren<Room>();
        player = GameObject.FindGameObjectWithTag("Player");
    }

    private void Update()
    {
        UpdateRoomProximity();
    }

    private void UpdateRoomProximity()
    {
        // Get player's position
        Vector3 playerPosition = player.transform.position;

        // Iterate through each room
        foreach (Room room in rooms)
        {
            // Create a path from player to room
            NavMeshPath path = new NavMeshPath();
            NavMesh.CalculatePath(playerPosition, room.transform.position, NavMesh.AllAreas, path);

            // Calculate the cost of the created path
            float pathCost = CalculatePathCost(path);

            // If the path cost is less than or equal to 50, set isNearby to true
            room.isNearbyPlayer = (pathCost <= 40f);
        }
    }

    public float CalculatePathCost(NavMeshPath path)
    {
        float cost = 0f;

        // Sum up the cost of each corner in the path
        for (int i = 1; i < path.corners.Length; i++)
        {
            cost += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return cost;
    }

    //------------------------------------------------
    // LOCATION CALCULATIONS \ WHERE SHOULD HUNTER GO
    //------------------------------------------------

    public Room GetFurthest(Vector3 agentLocation)
    {
        float maxDistance = float.MinValue;
        Room furthestRoom = null;

        foreach (Room room in rooms)
        {
            if (room.isNearbyPlayer) // Check if the room is nearby the player
            {
                float roomDistance = Vector3.Distance(room.transform.position, agentLocation);
                if (roomDistance > maxDistance)
                {
                    maxDistance = roomDistance;
                    furthestRoom = room;
                }
            }
        }

        return furthestRoom;
    }

    public Room GetSecondClosest(Vector3 agentLocation)
    {
        Room closestRoom = GetFurthest(agentLocation);
        float closestDistance = Vector3.Distance(closestRoom.transform.position, agentLocation);

        Room secondClosestRoom = null;
        float secondClosestDistance = float.MaxValue;

        foreach (Room room in rooms)
        {
            if (room.isNearbyPlayer) // Check if the room is nearby the player
            {
                float roomDistance = Vector3.Distance(room.transform.position, agentLocation);

                if (roomDistance < closestDistance)
                {
                    secondClosestRoom = closestRoom;
                    secondClosestDistance = closestDistance;

                    closestRoom = room;
                    closestDistance = roomDistance;
                }
                else if (roomDistance < secondClosestDistance)
                {
                    secondClosestRoom = room;
                    secondClosestDistance = roomDistance;
                }
            }
        }

        return secondClosestRoom;
    }

    public Room LeastCostMovement(Vector3 agentLocation, Vector3 targetLocation)
    {
        Room roomWithLowestCost = GetFurthest(agentLocation);
        float leastCost = float.MaxValue - 1;

        Room roomWithSecondLowestCost = null;
        float secondLeastCost = float.MaxValue;

        foreach (Room room in rooms)
        {
            if (room.isNearbyPlayer) // Check if the room is nearby the player
            {
                float distanceToTarget = Vector3.Distance(room.transform.position, targetLocation);
                if (distanceToTarget < 20f)
                {
                    // Calculate the path
                    NavMeshPath path = new NavMeshPath();
                    NavMesh.CalculatePath(agentLocation, room.transform.position, NavMesh.AllAreas, path);

                    // Check if the path is valid
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        // Calculate the cost of the path
                        float pathCost = CalculatePathCostFOR(path);

                        // Update the highest cost and room if needed
                        if (pathCost < leastCost)
                        {
                            secondLeastCost = leastCost;
                            roomWithSecondLowestCost = roomWithLowestCost;

                            leastCost = pathCost;
                            roomWithLowestCost = room;
                        }
                        else if (pathCost < secondLeastCost)
                        {
                            secondLeastCost = leastCost;
                            roomWithSecondLowestCost = roomWithLowestCost;
                        }
                    }
                    else
                    {
                        Debug.LogError("Failed to calculate path to " + room.name + "!");
                    }
                }
            }
        }

        return roomWithLowestCost;
    }

    public Room MostCostMovement(Vector3 agentLocation)
    {
        float highestCost = 0f;
        Room roomWithHighestCost = null;

        foreach (Room room in rooms)
        {
            if (room.isNearbyPlayer) // Check if the room is nearby the player
            {
                NavMeshPath path = new NavMeshPath();
                NavMesh.CalculatePath(agentLocation, room.transform.position, NavMesh.AllAreas, path);

                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    float pathCost = CalculatePathCostFOR(path);

                    if (pathCost > highestCost)
                    {
                        highestCost = pathCost;
                        roomWithHighestCost = room;
                    }
                }
            }
        }

        return roomWithHighestCost;
    }

    private float CalculatePathCostFOR(NavMeshPath path)
    {
        float cost = 0f;

        // Sum up the cost of each corner in the path
        for (int i = 1; i < path.corners.Length; i++)
        {
            cost += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return cost;
    }


    //------------------------------------------------
    // LOCATION CALCULATIONS \ WHERE SHOULD HUNTER GO
    //------------------------------------------------
    public Vector3 PosNearPlayer()
    {

        NavMeshPath path = new NavMeshPath();
        if (GetComponent<Director>().hunterAgent.CalculatePath(player.transform.position, path))
        {
            // Make sure there is more than one corner
            if (path.corners.Length > 1)
            {
                // The second-to-last corner is the new endpoint
                return path.corners[path.corners.Length - 2];
            }
            // If there's only one corner, use it as the endpoint
            else if (path.corners.Length == 1)
            {
                return path.corners[0];
            }
        }
        return Vector3.zero;
    }
    public Vector3 PosFarFromPlayer()
    {
        Room targetRoom = MostCostMovement(player.transform.position);
        NavMeshPath path = new NavMeshPath();
        if (GetComponent<Director>().hunterAgent.CalculatePath(targetRoom.transform.position, path))
        {

            if (path.corners.Length > 1) // Ensure there is more than one corner
            {
                // The second-to-last corner is the new endpoint
                return path.corners[path.corners.Length - 2];
            }
            else if (path.corners.Length == 1) // If there's only one corner, use it as the endpoint
            {
                return path.corners[0];
            }
        }

        return Vector3.zero;
    }
    public Vector3 FurthestRoom()
    {
        return MostCostMovement(player.transform.position).transform.position;
    }

    public Vector3 ClosestRoom()
    {
        return LeastCostMovement(GetComponent<Director>().hunter.position, player.transform.position).transform.position;
    }

}

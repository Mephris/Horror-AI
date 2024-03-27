using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Rooms : MonoBehaviour
{

    private Room[] rooms;

    private void Awake()
    {
        rooms = GetComponentsInChildren<Room>();
    }

    public Room GetFurthest(Vector3 agentLocation)
    {
        float maxDistance = float.MinValue;
        Room furthestRoom = null;

        foreach (Room room in rooms)
        {
            float roomDistance = Vector3.Distance(room.transform.position, agentLocation);
            if (roomDistance > maxDistance)
            {
                maxDistance = roomDistance;
                furthestRoom = room;
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

        return secondClosestRoom;
    }
    // Agent = Enemy, target = player
    public Room LeastCostMovement(Vector3 agentLocation, Vector3 targetLocation)
    {
        Room roomWithLowestCost = GetFurthest(agentLocation);
        float leastCost = float.MaxValue - 1;

        Room roomWithSecondLowestCost = null;
        float secondLeastCost = float.MaxValue;

        foreach (Room room in rooms)
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
                    float pathCost = CalculatePathCost(path);

                    // Update the highest cost and room if needed
                    if (pathCost < leastCost)
                    {
                        secondLeastCost = leastCost;
                        roomWithSecondLowestCost = roomWithLowestCost;

                        leastCost = pathCost;
                        roomWithLowestCost = room;
                    }
                    else if(pathCost < secondLeastCost)
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

        return roomWithLowestCost;
    }

    public Room MostCostMovement(Vector3 agentLocation)
    {
        float highestCost = 0f;
        Room roomWithHighestCost = null;

        foreach (Room room in rooms)
        {
            NavMeshPath path = new NavMeshPath();
            NavMesh.CalculatePath(agentLocation, room.transform.position, NavMesh.AllAreas, path);

            if (path.status == NavMeshPathStatus.PathComplete)
            {
                float pathCost = CalculatePathCost(path);

                if (pathCost > highestCost)
                {
                    highestCost = pathCost;
                    roomWithHighestCost = room;
                }
            }
        }

        return roomWithHighestCost;
    }

    private float CalculatePathCost(NavMeshPath path)
    {
        float cost = 0f;

        // Sum up the cost of each corner in the path
        for (int i = 1; i < path.corners.Length; i++)
        {
            cost += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return cost;
    }
}

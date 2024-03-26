using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rooms : MonoBehaviour
{

    private Room[] rooms;

    private void Awake()
    {
        rooms = GetComponentsInChildren<Room>();
    }

    public Room GetFurthestRoom(Vector3 agentLocation)
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

    public Room GetSecondClosestRoom(Vector3 agentLocation)
    {
        Room closestRoom = GetFurthestRoom(agentLocation);
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
}

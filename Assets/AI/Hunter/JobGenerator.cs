using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public static class JobGenerator
{
    public static Queue<HunterJob> GenerateRoomClear(RoomInfo targetRoom, RoomInfo currentRoom)
    {
        Queue<HunterJob> queue = new Queue<HunterJob>();

        if (targetRoom == null) return queue;

        // --- STEP 1: THE DOOR ---
        // Find a specific Doorway Point in the target room
        Transform doorPoint = FindConnectingDoor(targetRoom);

        if (doorPoint != null)
        {
            // 1. Stalk the Door (Move)
            queue.Enqueue(HunterJob.CreateStalk(doorPoint));

            // 2. Peek (Action)
            queue.Enqueue(HunterJob.CreateAction(JobType.Action, doorPoint, 2.0f));
        }

        // --- STEP 2: THE INSIDE ---
        // Find the hottest Standard Point (Table, Vent) inside the room
        Transform insidePoint = GetBestSpotInRoom(targetRoom);

        if (insidePoint != null)
        {
            // 3. Move Inside (Targeting the Table, not the floor)
            HunterJob enterJob = HunterJob.CreateMoveTo(insidePoint.position);
            enterJob.targetInterest = insidePoint; // <--- CRITICAL: Allows Cooling
            enterJob.speed = 0.6f;
            queue.Enqueue(enterJob);

            // 4. Scan the Table (Action)
            queue.Enqueue(HunterJob.CreateAction(JobType.Wait, insidePoint, 3.0f));
        }
        else
        {
            // Emergency: If room has NO points, just wait at the door
            if (doorPoint != null)
                queue.Enqueue(HunterJob.CreateAction(JobType.Wait, doorPoint, 2.0f));
        }

        return queue;
    }

    private static Transform FindConnectingDoor(RoomInfo target)
    {
        // Return the Transform of the first Doorway in the list
        foreach (var mem in target.patrolPoints)
        {
            if (mem.pointType == PointType.Doorway) return mem.pointTransform;
        }
        return null;
    }

    private static Transform GetBestSpotInRoom(RoomInfo room)
    {
        // 1. Sort by Heat (Desc) to find the most interesting point
        var bestMem = room.patrolPoints
            .Where(p => p.pointType == PointType.Standard) // Ignore doors
            .OrderByDescending(p => p.playerProbability)
            .FirstOrDefault();

        if (bestMem != null) return bestMem.pointTransform;

        // 2. Fallback: Any non-door point
        var anyMem = room.patrolPoints.FirstOrDefault(p => p.pointType != PointType.Standard);
        if (anyMem != null) return anyMem.pointTransform;

        return null;
    }
}
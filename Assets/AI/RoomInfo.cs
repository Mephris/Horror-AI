using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class RoomInfo
{
    public string roomName;
    public int exitCount = 0; // You would set this on initialization

    // This is the "Level 1" aggregated heat for the Planner.
    public float generalCuriosity = 0f;

    // This is the "Level 2" micro data.
    // It's a List of *references* to the memory objects,
    // which are also stored in the main HunterAI dictionary.
    public List<HunterPatrolMemory> patrolPoints;

    public RoomInfo(string name, int exits)
    {
        this.roomName = name;
        this.exitCount = exits;
        this.patrolPoints = new List<HunterPatrolMemory>();
    }

    // This method is called by HunterAI to update the room's "heat."
    public void UpdateGeneralCuriosity()
    {
        if (patrolPoints.Count == 0)
        {
            generalCuriosity = 0;
            return;
        }

        // Calculate the "heat" (e.g., average probability of all points)
        float totalProbability = 0f;
        foreach (HunterPatrolMemory pointMemory in patrolPoints)
        {
            totalProbability += pointMemory.playerProbability;
        }

        generalCuriosity = totalProbability / patrolPoints.Count;
    }

    // Helper to check if all points in this room are "cold."
    public bool IsFullyPatrolled(float baseUncertainty)
    {
        foreach (HunterPatrolMemory pointMemory in patrolPoints)
        {
            // If any point is still "hot" (above base), this room is not done.
            if (pointMemory.playerProbability > baseUncertainty)
                return false;
        }
        // All points are "cold."
        return true;
    }
}
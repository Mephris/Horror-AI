using System.Collections.Generic;
using UnityEngine;

public enum GoalType
{
    None,         // Idle or looking for a new goal
    SearchRoom,   // Systematically patrol a "hot" room
    AmbushRoom,   // A tactical goal, e.g., for a dead-end room
    InvestigatePosition, // A high-priority goal from a sound or Director hint
    ShortPatrol   // A short, calculated route through hot points or rooms
}

[System.Serializable]
public class HunterGoal
{
    public GoalType type;
    public RoomInfo targetRoom;      // The Room this goal applies to (e.g., "Search KITCHEN")
    public Vector3 targetPosition; // The specific point this goal applies to (e.g., "Investigate SOUND")

    public List<Transform> patrolSteps = new List<Transform>();
    public HunterGoal(GoalType type, RoomInfo room = null, Vector3 pos = default, List<Transform> steps = null)
    {
        this.type = type;
        this.targetRoom = room;
        this.targetPosition = pos;

        // Optional patrol steps
        if (steps != null)
        {
            this.patrolSteps = steps;
        }
    }
}  // A simple struct to hold the current goal for the Hunter AI
using System.Collections.Generic;
using UnityEngine;

public enum GoalType
{
    None,         // Idle or looking for a new goal
    SearchRoom,   // Systematically patrol a "hot" room
    AmbushRoom,   // A tactical goal, e.g., for a dead-end room
    InvestigatePosition, // A high-priority goal from a sound or Director hint
    FreePatrol    // NEW: Reactive, free-roaming patrol looking for nearby hot points
}

[System.Serializable]
public class HunterGoal
{
    public GoalType type;
    public RoomInfo targetRoom;      // The Room this goal applies to (e.g., "Search KITCHEN")
    public Vector3 targetPosition;   // The specific point this goal applies to (e.g., "Investigate SOUND")

    public HunterGoal(GoalType type, RoomInfo room = null, Vector3 pos = default)
    {
        this.type = type;
        this.targetRoom = room;
        this.targetPosition = pos;
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public static class Actions
{
    //Director Commands
    public static Action<Vector3> CommandToMove;
    public static Action<Vector3> HighPriorityCommandToMove;

    //Events which are happening
    public static Action<bool> PlayerCanSeeHunter;
    public static Action<bool, Vector3> HunterCanSeePlayer;
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PatrolPoints : MonoBehaviour
{
    public bool isChecked = false;

    public void ToggleCheckStatus()
    {
        isChecked = true;
        Debug.Log($"Patrol point is {(isChecked ? "checked" : "unchecked")}");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.2f);
    }
}

// --- PatrolPoints.cs ---

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PatrolPoints : MonoBehaviour
{
    // RENAMED
    public bool HasBeenVisited = false;

    public void ToggleCheckStatus()
    {
        HasBeenVisited = true;
        Debug.Log($"Patrol point is {(HasBeenVisited ? "visited" : "unvisited")}");
        StartCoroutine(ResetCheckStatusAfterDelay(50));
    }

    public void ResetCheckStatus()
    {
        HasBeenVisited = false;
        Debug.Log($"Patrol point is {(HasBeenVisited ? "visited" : "unvisited")} after reset");
    }

    private IEnumerator ResetCheckStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HasBeenVisited = false;
        Debug.Log($"Patrol point is {(HasBeenVisited ? "visited" : "unvisited")} after delay");
    }

    private void OnDrawGizmos()
    {
        // Use HasBeenVisited for Gizmos
        if (HasBeenVisited)
        {
            Gizmos.color = Color.green;
        }
        else
        {
            Gizmos.color = Color.red;
        }

        Gizmos.DrawCube(transform.position, Vector3.one * 0.2f);
    }
}
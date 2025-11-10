using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PatrolPoints : MonoBehaviour
{
    public bool HasBeenVisited = false;

    public void ToggleCheckStatus()
    {
        HasBeenVisited = true;
        Debug.Log($"Patrol point is {(HasBeenVisited ? "checked" : "unchecked")}");
        StartCoroutine(ResetCheckStatusAfterDelay(50));
    }

    public void ResetCheckStatus()
    {
        HasBeenVisited = false;
        Debug.Log($"Patrol point is {(HasBeenVisited ? "checked" : "unchecked")} after reset");
    }

    private IEnumerator ResetCheckStatusAfterDelay(float delay)
    {
        // Wait for the specified delay
        yield return new WaitForSeconds(delay);

        // Reset HasBeenVisited to false after the delay
        HasBeenVisited = false;
        Debug.Log($"Patrol point is {(HasBeenVisited ? "checked" : "unchecked")} after delay");
    }

    private void OnDrawGizmos()
    {
        
        if(HasBeenVisited)
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

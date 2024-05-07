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
        StartCoroutine(ResetCheckStatusAfterDelay(40+(Director.calculationInterval*5)));
    }

    public void ResetCheckStatus()
    {
        isChecked = false;
        Debug.Log($"Patrol point is {(isChecked ? "checked" : "unchecked")} after reset");
    }

    private IEnumerator ResetCheckStatusAfterDelay(float delay)
    {
        // Wait for the specified delay
        yield return new WaitForSeconds(delay);

        // Reset isChecked to false after the delay
        isChecked = false;
        Debug.Log($"Patrol point is {(isChecked ? "checked" : "unchecked")} after delay");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.2f);
    }
}

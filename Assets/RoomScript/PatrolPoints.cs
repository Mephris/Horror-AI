// --- PatrolPoints.cs ---

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PatrolPoints : MonoBehaviour
{
    // Atrefacts
    /* public bool HasBeenVisited = false;

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
    */

    private Hunter_Basic hunterBasic;
    private Hunter_Basic HunterBasic
    {
        get
        {
            if (hunterBasic == null)
            {
                // Find the Hunter in the scene (assuming there is only one or it's tagged uniquely)
                hunterBasic = FindObjectOfType<Hunter_Basic>();
            }
            return hunterBasic;
        }
    }

    private void OnDrawGizmos()
    {
        // 1. Get the probability score from the Hunter's memory
        float probability = 0f;

        if (HunterBasic != null)
        {
            // Use the new helper function in Hunter_Basic
            probability = HunterBasic.GetProbabilityScore(this.transform);
        }

        // 2. Set Gizmo color based on probability (0.0 to 1.0)
        // Color.Lerp interpolates between two colors:
        // probability = 0.0 -> Color.green (Low Priority)
        // probability = 1.0 -> Color.red (High Priority)
        Gizmos.color = Color.Lerp(Color.green, Color.red, probability);


        // 3. Draw the Gizmo
        // Scale the gizmo based on probability to make high-priority points stand out visually.
        float scale = 0.2f + (probability * 0.15f); // Scale from 0.2 to 0.35
        Gizmos.DrawCube(transform.position, Vector3.one * scale);

        // Optional: Draw a label with the probability score (Editor only)
        // You may need to add 'using UnityEditor;' at the top of PatrolPoints.cs if you use this.
#if UNITY_EDITOR
        // UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, $"Prob: {probability:F2}");
#endif
    }
}
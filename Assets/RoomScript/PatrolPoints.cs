using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// You may need to add this if you're using Unity Editor visualization helpers
// #if UNITY_EDITOR
// using UnityEditor; 
// #endif

public class PatrolPoints : MonoBehaviour
{
    // --- OBSOLETE: Fields and methods for FSM state are removed ---
    // public bool HasBeenVisited = false;
    // public void ToggleCheckStatus() { ... }
    // public void ResetCheckStatus() { ... }
    // private IEnumerator ResetCheckStatusAfterDelay(float delay) { ... }

    // -----------------------------------------------------------------------------------

    // Cache the Hunter_Basic component for Gizmo drawing
    // This allows the PatrolPoint to query the Hunter's current memory for visualization.
    private Hunter_Basic hunterBasic;
    private Hunter_Basic HunterBasic
    {
        get
        {
            if (hunterBasic == null)
            {
                // Find the Hunter in the scene (assuming only one Hunter_Basic exists)
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
            // Query the memory via the Hunter_Basic helper method
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
    }
}
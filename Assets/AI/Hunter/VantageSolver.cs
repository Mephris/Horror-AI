using UnityEngine;
using UnityEngine.AI;

public static class VantageSolver
{
    // Finds a valid position on the NavMesh 'distance' meters away from the target
    // in the direction of the observer (Hunter).
    public static Vector3 GetVantagePosition(Transform target, Vector3 observerPosition, float optimalDistance = 3.0f)
    {
        if (target == null) return observerPosition;

        // 1. Calculate the ideal vector (Backing away from the target towards the Hunter)
        Vector3 targetPos = target.position;
        Vector3 dirToHunter = (observerPosition - targetPos).normalized;

        // If hunter is exactly on top of target, pick a random direction
        if (dirToHunter == Vector3.zero) dirToHunter = Vector3.forward;

        // 2. Determine the Ideal Point
        Vector3 idealPoint = targetPos + (dirToHunter * optimalDistance);

        // 3. Snap to NavMesh (Finding the floor)
        NavMeshHit hit;
        // Search within 2.0f of the ideal point to find valid ground
        if (NavMesh.SamplePosition(idealPoint, out hit, 2.0f, NavMesh.AllAreas))
        {
            // 4. Line of Sight Check (Optional but recommended)
            // Ensure no wall exists between the Vantage Point and the Target
            // We lift the check 1.5m up to simulate "Eye Level"
            Vector3 eyeLevelStart = hit.position + Vector3.up * 1.5f;
            Vector3 eyeLevelEnd = targetPos + Vector3.up * 1.5f;

            // Simple raycast check (Blocked by Default/Walls)
            // (You might need to adjust the layer mask based on your project layers)
            if (!Physics.Linecast(eyeLevelStart, eyeLevelEnd, LayerMask.GetMask("Default")))
            {
                return hit.position; // Found a perfect spot!
            }
        }

        // 5. Fallback
        // If the 3m spot is invalid (inside a wall or blocked), we default to 
        // the object's actual position so the Hunter at least goes there.
        return targetPos;
    }
}
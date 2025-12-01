using UnityEngine;
using UnityEngine.AI;

public static class VantageSolver
{
    // Settings for the "Query"
    private static float eyeHeight = 1.6f; // Hunter's eye level
    private static int raycastMask = LayerMask.GetMask("Default", "Wall", "Environment"); // What blocks vision?

    public static Vector3 GetVantagePosition(Transform target, Vector3 hunterPos, float idealDistance = 3.0f)
    {
        if (target == null) return hunterPos;

        Vector3 targetPos = target.position;
        Vector3 dirToHunter = (hunterPos - targetPos).normalized;
        if (dirToHunter == Vector3.zero) dirToHunter = Vector3.forward;

        // --- 1. GENERATE CANDIDATES (The Semi-Circle) ---
        // We try 3 angles: Direct (0), Left (-45), Right (+45)
        float[] angles = { 0f, -45f, 45f };

        foreach (float angle in angles)
        {
            // Rotate the direction vector
            Vector3 candidateDir = Quaternion.Euler(0, angle, 0) * dirToHunter;
            Vector3 candidatePos = targetPos + (candidateDir * idealDistance);

            // --- 2. FILTER (NavMesh Validity) ---
            NavMeshHit hit;
            // Check if point is on NavMesh (Search radius 1.0m)
            if (NavMesh.SamplePosition(candidatePos, out hit, 1.0f, NavMesh.AllAreas))
            {
                Vector3 validFloorPos = hit.position;

                // --- 3. FILTER (Visibility / 3D Raycast) ---
                // Check if we can actually SEE the target from here
                Vector3 rayStart = validFloorPos + Vector3.up * eyeHeight;
                Vector3 rayEnd = targetPos + Vector3.up * (eyeHeight * 0.8f); // Look slightly below top

                if (!Physics.Linecast(rayStart, rayEnd, raycastMask))
                {
                    // SUCCESS: Found a valid, visible, walkable point.
                    // Since we check "0" angle first, this is automatically the best one.
                    return validFloorPos;
                }
            }
        }

        // FALLBACK: If all 3m spots are blocked (tight corner), try closer (1.5m)
        // Recursive call with smaller distance
        if (idealDistance > 1.5f)
        {
            return GetVantagePosition(target, hunterPos, idealDistance/2.0f);
        }

        // FINAL FALLBACK: Just go to the object itself
        return targetPos;
    }
}
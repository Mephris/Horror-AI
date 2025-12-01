using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class VantageSolver
{
    // Settings
    private static float eyeHeight = 1.6f;
    private static int raycastMask = LayerMask.GetMask("Default", "Wall", "Environment", "Obstruction");

    // --- DEBUG DATA (Static memory for Gizmos) ---
    public static Vector3 DebugBestPoint;
    public static List<Vector3> DebugCandidates = new List<Vector3>();
    public static List<string> DebugLabels = new List<string>(); // Stores "L", "C", "R"

    public static Vector3 GetVantagePosition(Transform target, Vector3 hunterPos, float idealDistance = 3.0f)
    {
        if (target == null) return hunterPos;

        // Clear old debug data
        DebugCandidates.Clear();
        DebugLabels.Clear();
        DebugBestPoint = Vector3.zero;

        Vector3 targetPos = target.position;
        Vector3 dirToHunter = (hunterPos - targetPos).normalized;
        if (dirToHunter == Vector3.zero) dirToHunter = Vector3.forward;

        // 1. GENERATE CANDIDATES (Center, Left, Right)
        // We use a Dictionary or parallel lists to map Angle -> Label
        float[] angles = { 0f, -45f, 45f };
        string[] labels = { "Center", "Left", "Right" };

        for (int i = 0; i < angles.Length; i++)
        {
            float angle = angles[i];
            string label = labels[i];

            Vector3 candidateDir = Quaternion.Euler(0, angle, 0) * dirToHunter;
            Vector3 candidatePos = targetPos + (candidateDir * idealDistance);

            // Add to Debug List (Raw position before checks)
            DebugCandidates.Add(candidatePos);
            DebugLabels.Add(label);

            // 2. FILTER (NavMesh Validity)
            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidatePos, out hit, 1.0f, NavMesh.AllAreas))
            {
                Vector3 validFloorPos = hit.position;

                // 3. FILTER (Visibility Raycast)
                Vector3 rayStart = validFloorPos + Vector3.up * eyeHeight;
                Vector3 rayEnd = targetPos + Vector3.up * (eyeHeight * 0.8f);

                if (!Physics.Linecast(rayStart, rayEnd, raycastMask))
                {
                    // SUCCESS!
                    DebugBestPoint = validFloorPos; // Store winner
                    return validFloorPos;
                }
            }
        }

        // FALLBACK: Recursion
        if (idealDistance > 1.5f)
        {
            return GetVantagePosition(target, hunterPos, 1.5f);
        }

        // FINAL FALLBACK
        DebugBestPoint = targetPos;
        return targetPos;
    }
}
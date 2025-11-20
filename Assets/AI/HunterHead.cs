using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HunterHeadController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HunterAI hunterAI;
    [SerializeField] private Transform headTransform;

    [Header("Settings")]
    [SerializeField] private float lookRadius = 25f; // Increased to 25m to match Central Vision
    [SerializeField] private float turnSpeed = 5.0f;
    [Range(45f, 120f)]
    [SerializeField] private float maxNeckAngle = 80f;

    [Header("Vision")]
    // Assign layers like "Default", "Walls", "Environment". 
    // Do NOT include "Player" or "Ignore Raycast".
    [SerializeField] private LayerMask obstructionMask;

    private Transform currentLookTarget;
    private float targetChangeTimer = 0f;

    private void Start()
    {
        if (hunterAI == null) hunterAI = GetComponent<HunterAI>();
        if (headTransform == null) headTransform = transform.Find("Head");
    }

    private void Update()
    {
        UpdateLookTarget();
        RotateHead();
    }

    private void UpdateLookTarget()
    {
        targetChangeTimer -= Time.deltaTime;
        if (targetChangeTimer > 0) return;

        // 1. Get points within 25m
        List<HunterPatrolMemory> nearbyPoints = hunterAI.GetLocalHotPoints(transform.position, lookRadius);

        Transform bestCandidate = null;
        float highestHeat = -1f;

        foreach (var mem in nearbyPoints)
        {
            Transform pointT = mem.patrolpointTransform;

            // A. Don't look at what the body is walking towards (it's redundant)
            if (pointT == hunterAI.currentPatrolTarget) continue;

            // B. Check Angle (Neck Limit)
            Vector3 dirToPoint = (pointT.position - headTransform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToPoint);

            if (angle < maxNeckAngle)
            {
                // C. Check Obstruction (Raycast) - The "Wall Hack" Fix
                float distToPoint = Vector3.Distance(headTransform.position, pointT.position);

                // Raycast from Head -> Target. If we hit something in the obstruction layer, we block it.
                if (!Physics.Raycast(headTransform.position, dirToPoint, distToPoint, obstructionMask))
                {
                    // Line of Sight is Clear!
                    if (mem.playerProbability > highestHeat)
                    {
                        highestHeat = mem.playerProbability;
                        bestCandidate = pointT;
                    }
                }
            }
        }

        currentLookTarget = bestCandidate;
        targetChangeTimer = 0.5f;
    }

    private void RotateHead()
    {
        Quaternion targetRotation;

        if (currentLookTarget != null)
        {
            Vector3 direction = currentLookTarget.position - headTransform.position;
            targetRotation = Quaternion.LookRotation(direction);
        }
        else
        {
            // Look forward relative to the body
            targetRotation = Quaternion.LookRotation(transform.forward);
        }

        // Clamp neck angle logic
        float angle = Quaternion.Angle(Quaternion.LookRotation(transform.forward), targetRotation);
        if (angle > maxNeckAngle)
        {
            targetRotation = Quaternion.LookRotation(transform.forward);
        }

        headTransform.rotation = Quaternion.Slerp(headTransform.rotation, targetRotation, Time.deltaTime * turnSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        if (headTransform == null) return;

        // Visualizing the radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(headTransform.position, lookRadius);

        // Visualizing the Raycast if target is found
        if (currentLookTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(headTransform.position, currentLookTarget.position);
        }
    }
}
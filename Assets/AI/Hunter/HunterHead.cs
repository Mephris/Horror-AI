using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq; // Added for Where/OrderBy functions

public class HunterHeadController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HunterAI hunterAI;
    [SerializeField] private Transform headTransform;

    [Header("Settings")]
    [SerializeField] private float lookRadius = 25f;
    [SerializeField] private float turnSpeed = 5.0f;
    [Range(45f, 120f)]
    [SerializeField] private float maxNeckAngle = 80f;

    [Header("Vision")]
    [SerializeField] private LayerMask obstructionMask;

    // State
    private Transform priorityTarget;
    private Vector3 idleLocalOffset; // Relative to the Hunter (e.g., "Forward 10, Up 1")
    private float targetChangeTimer = 0f;
    private float idleTimer = 0f;
    private bool isIdling = false;

    private void Start()
    {
        if (hunterAI == null) hunterAI = GetComponent<HunterAI>();
        if (headTransform == null) headTransform = transform.Find("Head");

        // Initialize with a default forward look
        idleLocalOffset = new Vector3(0, 0, 10f);
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

        targetChangeTimer = 0.2f; // Check for hot targets 5 times a second

        // 1. PRIORITY CHECK: Look for Hot Patrol Points
        // FIX: The function is now correctly called on the HunterAI instance.
        List<HunterPatrolMemory> nearbyPoints = hunterAI.GetLocalHotPoints(transform.position, lookRadius);
        Transform bestCandidate = null;
        float highestHeat = -1f;

        foreach (var mem in nearbyPoints)
        {
            Transform pointT = mem.patrolpointTransform;
            // FIX: Uses the new variable name
            if (pointT == hunterAI.currentInterestTarget) continue;

            Vector3 dirToPoint = (pointT.position - headTransform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToPoint);

            if (angle < maxNeckAngle)
            {
                float distToPoint = Vector3.Distance(headTransform.position, pointT.position);
                if (!Physics.Raycast(headTransform.position, dirToPoint, distToPoint, obstructionMask))
                {
                    if (mem.playerProbability > highestHeat)
                    {
                        highestHeat = mem.playerProbability;
                        bestCandidate = pointT;
                    }
                }
            }
        }

        priorityTarget = bestCandidate;

        // 2. IDLE CHECK (Runs if no hot points were found)
        if (priorityTarget == null)
        {
            isIdling = true;
            idleTimer -= Time.deltaTime;

            if (idleTimer <= 0f)
            {
                // PICK NEW RANDOM OFFSET
                float randomX = Random.Range(-5f, 5f);
                float randomY = Random.Range(-1f, 2f);
                float forwardDist = 10f;

                idleLocalOffset = new Vector3(randomX, randomY, forwardDist);

                // FIX: Uses the now-required setting from HunterAI
                idleTimer = hunterAI.idleLookInterval + Random.Range(-0.5f, 0.5f);
            }
        }
        else
        {
            isIdling = false;
        }
    }

    private void RotateHead()
    {
        Quaternion targetRotation;
        float speed = turnSpeed;

        if (priorityTarget != null)
        {
            Vector3 direction = priorityTarget.position - headTransform.position;
            targetRotation = Quaternion.LookRotation(direction);
        }
        else
        {
            // IDLE: Look at the Moving Target (Virtual Child)
            Vector3 worldIdleTarget = transform.TransformPoint(idleLocalOffset);

            Vector3 direction = worldIdleTarget - headTransform.position;

            if (direction != Vector3.zero)
                targetRotation = Quaternion.LookRotation(direction);
            else
                targetRotation = Quaternion.LookRotation(transform.forward);

            // FIX: Uses the now-required setting from HunterAI
            speed = hunterAI.idleHeadTurnSpeed;
        }

        // Clamp Neck
        float angle = Quaternion.Angle(Quaternion.LookRotation(transform.forward), targetRotation);
        if (angle > maxNeckAngle)
        {
            targetRotation = Quaternion.LookRotation(transform.forward);
        }

        headTransform.rotation = Quaternion.Slerp(headTransform.rotation, targetRotation, Time.deltaTime * speed);
    }

    private void OnDrawGizmosSelected()
    {
        if (headTransform == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(headTransform.position, lookRadius);

        if (isIdling)
        {
            Vector3 worldIdleTarget = transform.TransformPoint(idleLocalOffset);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(worldIdleTarget, 0.5f);
            Gizmos.DrawLine(headTransform.position, worldIdleTarget);
        }
        else if (priorityTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(headTransform.position, priorityTarget.position);
        }
    }
}
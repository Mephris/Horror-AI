using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    private Transform priorityTarget; // Actual Patrol Point
    private Vector3 idleTarget;       // Random spot in the roomdsgrfggfdgdfdgfgdfdfggdfdgfdgfdgf
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
        List<HunterPatrolMemory> nearbyPoints = hunterAI.GetLocalHotPoints(transform.position, lookRadius);
        Transform bestCandidate = null;
        float highestHeat = -1f;

        foreach (var mem in nearbyPoints)
        {
            Transform pointT = mem.patrolpointTransform;
            if (pointT == hunterAI.currentPatrolTarget) continue; // Don't look at walk target

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

        // 2. IDLE CHECK
        if (priorityTarget == null)
        {
            isIdling = true;
            idleTimer -= Time.deltaTime;

            if (idleTimer <= 0f)
            {
                // PICK NEW RANDOM OFFSET (Relative to Body)
                // Instead of a world point, we pick a direction relative to "Forward"

                // Randomize slightly left/right (X) and up/down (Y)
                // We keep Z (Forward) strong so he mostly looks ahead while walking
                float randomX = Random.Range(-5f, 5f);
                float randomY = Random.Range(-1f, 2f); // Look slightly up at head height
                float forwardDist = 10f;

                idleLocalOffset = new Vector3(randomX, randomY, forwardDist);

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
            // PRIORITY: Look at specific world object
            Vector3 direction = priorityTarget.position - headTransform.position;
            targetRotation = Quaternion.LookRotation(direction);
        }
        else
        {
            // IDLE: Look at the Moving Target
            // 1. Convert Local Offset to World Space based on current Body Position/Rotation
            // This effectively treats the target as a "Child" of the Hunter
            Vector3 worldIdleTarget = transform.TransformPoint(idleLocalOffset);

            // 2. Calculate direction
            Vector3 direction = worldIdleTarget - headTransform.position;
            
            if (direction != Vector3.zero)
                targetRotation = Quaternion.LookRotation(direction);
            else
                targetRotation = Quaternion.LookRotation(transform.forward);
                
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
            // Visualize where the "Virtual Child" is currently floating
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
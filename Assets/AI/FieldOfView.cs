using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FieldOfView : MonoBehaviour
{
    [Header("FOV range")]
    public float radius;
    [Range(0, 360)]
    public float angle;

    [Header("Player")]
    public GameObject targetObjRef;


    [Header("Masks")]
    [SerializeField] private LayerMask targetMask;
    [SerializeField] private LayerMask obstructionMask;

    // These variables hold the state for the Hunter's perception
    public bool canSeeTarget;
    public Vector3 lastSeenTargetLocation;

    private void Start() => StartCoroutine(FOVRoutine());

    private IEnumerator FOVRoutine()
    {
        // Check frequency: A slower update rate is often better for AI perception
        WaitForSeconds wait = new WaitForSeconds(0.2f);
        while (true)
        {
            yield return wait;
            FieldOfViewCheck();

            // The Hunter (Enemy) FOV check
            if (this.gameObject.CompareTag("Enemy"))
            {
                if (targetObjRef != null && targetObjRef.CompareTag("Player")) // Only check for the player here
                {
                    // Broadcast the result to the Hunter_Basic component
                    Actions.HunterCanSeePlayer?.Invoke(canSeeTarget, lastSeenTargetLocation);

                    if (canSeeTarget)
                    {
                        // Store the player's position when they were seen
                        lastSeenTargetLocation = targetObjRef.transform.position;
                    }
                }
            }
            // Future: Player's FOV check (for player HUD/visuals)
            else if (this.gameObject.CompareTag("Player"))
            {
                if (targetObjRef != null && targetObjRef.CompareTag("Enemy"))
                {
                    Actions.PlayerCanSeeHunter?.Invoke(canSeeTarget);
                }
            }
        }
    }

    private void FieldOfViewCheck()
    {
        // Find all colliders within the defined radius
        Collider[] rangeChecks = Physics.OverlapSphere(transform.position, radius, targetMask);

        bool playerWasSeenThisFrame = false;

        if (rangeChecks.Length > 0)
        {
            foreach (Collider col in rangeChecks)
            {
                Transform target = col.transform;
                Vector3 directionToTarget = (target.position - transform.position).normalized;

                // Check if the target is within the FOV angle
                if (Vector3.Angle(transform.forward, directionToTarget) < angle / 2)
                {
                    float distanceToTarget = Vector3.Distance(transform.position, target.position);

                    // Check for obstruction (Raycast from Hunter's position to the target's position)
                    if (!Physics.Raycast(transform.position, directionToTarget, distanceToTarget, obstructionMask))
                    {
                        // --- We have line of sight to a target ---

                        // 1. Check if it's the main player target
                        if (target.gameObject == targetObjRef && targetObjRef.CompareTag("Player"))
                        {
                            // Set the flag for the player (canSeeTarget)
                            playerWasSeenThisFrame = true;
                        }

                        // 2. Check if it's a Patrol Point (for observation)
                        if (target.CompareTag("PatrolPoint"))
                        {
                            // Fire the action with the specific patrol point's transform
                            Actions.HunterSawPatrolPoint?.Invoke(target);
                        }
                    }
                }
            }
        }

        // Update the main 'canSeeTarget' flag outside the loop
        canSeeTarget = playerWasSeenThisFrame;
    }

}
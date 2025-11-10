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

    public bool canSeeTarget;
    public Vector3 lastSeenTargetLocation;

    private void Start() => StartCoroutine(FOVRoutine());

    private IEnumerator FOVRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(0.5f);
        while (true)
        {
            yield return wait;
            FieldOfViewCheck(); // <-- FIX 1: Call the check method

            // This part of your logic remains the same
            if (this.gameObject.CompareTag("Player"))
            {
                if (targetObjRef.CompareTag("Enemy"))
                {
                    Actions.PlayerCanSeeHunter(canSeeTarget);
                }
            }
            else if (this.gameObject.CompareTag("Enemy"))
            {
                if (targetObjRef.CompareTag("Player")) // Only check for the player here
                {
                    if (canSeeTarget)
                    {
                        lastSeenTargetLocation = targetObjRef.transform.position;
                        Actions.HunterCanSeePlayer(canSeeTarget, lastSeenTargetLocation);
                    }
                    else
                    {
                        // Send a "false" signal if the player is the target but not seen
                        Actions.HunterCanSeePlayer(canSeeTarget, lastSeenTargetLocation);
                    }
                }

                // We removed the patrol point check from here because
                // FieldOfViewCheck() now handles it directly.
            }
        }
    }

    // This is a suggested robust version of FieldOfViewCheck
    private void FieldOfViewCheck()
    {
        bool playerWasSeenThisFrame = false; // We track the player separately

        // Find all colliders (Player and Patrol Points) in range
        Collider[] rangeChecks = Physics.OverlapSphere(transform.position, radius, targetMask);

        if (rangeChecks.Length != 0)
        {
            foreach (Collider col in rangeChecks) // *** CORRECTED: Loop through ALL found colliders ***
            {
                Transform target = col.transform;
                Vector3 directionToTarget = (target.position - transform.position).normalized;

                if (Vector3.Angle(transform.forward, directionToTarget) < angle / 2)
                {
                    float distanceToTarget = Vector3.Distance(transform.position, target.position);

                    // Check for line of sight (Raycast against obstructionMask)
                    if (!Physics.Raycast(transform.position, directionToTarget, distanceToTarget, obstructionMask))
                    {
                        // --- We have line of sight to a target ---

                        // 1. Check if it's the main player target
                        if (target.gameObject == targetObjRef && targetObjRef.CompareTag("Player"))
                        {
                            // Set the flag for the player (canSeeTarget)
                            playerWasSeenThisFrame = true;
                        }

                        // 2. Check if it's a Patrol Point
                        // Note: This action should fire regardless of whether the player is also seen this frame.
                        if (target.CompareTag("PatrolPoint"))
                        {
                            // Fire the action with the specific patrol point's transform
                            Actions.HunterSawPatrolPoint?.Invoke(target);
                            // Optional: Add a Debug.Log here to confirm it fires!
                            // Debug.Log($"Hunter saw Patrol Point: {target.name}"); 
                        }
                    }
                }
            }
        }

        // Update the main 'canSeeTarget' flag outside the loop
        // This flag determines if the Hunter can see the Player this frame.
        canSeeTarget = playerWasSeenThisFrame;

        // If no targets are in range, reset canSeeTarget
        if (rangeChecks.Length == 0 && canSeeTarget)
        {
            canSeeTarget = false;
        }
    }

}

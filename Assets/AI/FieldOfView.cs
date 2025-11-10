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
        bool playerWasSeenThisFrame = false;

        // Check for both the Player's Layer and the PatrolPointLayer
        Collider[] rangeChecks = Physics.OverlapSphere(transform.position, radius, targetMask);

        if (rangeChecks.Length != 0)
        {
            foreach (Collider col in rangeChecks) // *** LOOP through ALL nearby colliders ***
            {
                Transform target = col.transform;
                Vector3 directionToTarget = (target.position - transform.position).normalized;

                if (Vector3.Angle(transform.forward, directionToTarget) < angle / 2)
                {
                    float distanceToTarget = Vector3.Distance(transform.position, target.position);

                    if (!Physics.Raycast(transform.position, directionToTarget, distanceToTarget, obstructionMask))
                    {
                        // --- We have line of sight to *something* ---

                        // 1. Check if it's our main target (the Player)
                        if (target.gameObject == targetObjRef)
                        {
                            playerWasSeenThisFrame = true;
                        }

                        // 2. Check if it's a Patrol Point and we are the Enemy
                        if (this.gameObject.CompareTag("Enemy") && target.CompareTag("PatrolPoint"))
                        {
                            Actions.HunterSawPatrolPoint?.Invoke(target);
                        }
                    }
                }
            }
        }

        // Update the main 'canSeeTarget' flag after checking all objects
        canSeeTarget = playerWasSeenThisFrame;
    }
}

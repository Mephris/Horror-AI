using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FieldOfView : MonoBehaviour
{
    [Header("FOV range")]
    public float radius;
    [Range(0,360)]
    public float angle;

    [Header("Player")]
    public GameObject targetObjRef;


    [Header("Masks")]
    [SerializeField] private LayerMask targetMask;
    [SerializeField] private LayerMask obstructionMask;

    public bool canSeeTarget;

    private void Start()
    {
        StartCoroutine(FOVRoutine());
        
    }

    private IEnumerator FOVRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(0.5f);
        while(true)
        {
            yield return wait;
            FieldOfViewCheck();
            if (targetObjRef.CompareTag("Player"))
            {
                Actions.PlayerCanSeeHunter(canSeeTarget);
            }
            else if(targetObjRef.CompareTag("Enemy"))
            {
                Actions.HunterCanSeePlayer(canSeeTarget);
            }
            
        }
    }

    private void FieldOfViewCheck()
    {
        Collider[] rangeChecks = Physics.OverlapSphere(transform.position, radius, targetMask);

        if (rangeChecks.Length != 0)
        {
            Transform target = rangeChecks[0].transform;
            Vector3 directionToTarget = (target.position - transform.position).normalized;

            if (Vector3.Angle(transform.forward, directionToTarget) < angle / 2)
            {
                float distanceToTarget = Vector3.Distance(transform.position, target.position);

                if (!Physics.Raycast(transform.position, directionToTarget, distanceToTarget, obstructionMask))
                    canSeeTarget = true;
                else
                    canSeeTarget = false;
            }
            else canSeeTarget = false;
        }
        else if (canSeeTarget)
            canSeeTarget = false;
        
        
    }
}

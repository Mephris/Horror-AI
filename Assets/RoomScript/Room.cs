using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Room : MonoBehaviour
{
    public PatrolPoints[] patrolPoint;

    public bool isNearbyPlayer;

    private void Awake()
    {
        patrolPoint = GetComponentsInChildren<PatrolPoints>();
    }


    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.3f);
    }
}

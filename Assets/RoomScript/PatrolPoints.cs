using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PatrolPoints : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.2f);
    }
}

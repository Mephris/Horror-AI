using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

public class Hunter_Basic : MonoBehaviour
{

    [SerializeField] private Transform targetPos;

    private NavMeshAgent agent;

    [Header("Current Task Priority")]
    public States states;
    public enum States
    {
        Patrol,
        Chase,
        Listen,
        ExecuteOrder
    }

    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove +=  OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;
    }

    // Update is called once per frame
    void Update()
    {

    }

    //--------
    // STATES
    //--------

    private void Patrol()
    {

    }

    private void Chase()
    {

    }

    private void Listen()
    {

    }


    //---------------------------
    //EVENTS TRIGGERED BY ACTIONS
    //---------------------------
    private void OnSeePlayer(bool obj)
    {
        if(obj)
            agent.SetDestination(GetComponent<FieldOfView>().targetObjRef.transform.position);
    }

    private void OnCommandToMove(Vector3 target)
    {
        agent.SetDestination(targetPos.position);
    }

    private void OnHighPriorityCommandToMove(Vector3 target)
    {
        agent.SetDestination(targetPos.position);
    }
}

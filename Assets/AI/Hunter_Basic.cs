using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

public class Hunter_Basic : MonoBehaviour
{

    [SerializeField] private Transform targetPos;

    private NavMeshAgent agent;

    private int currentRandomIndex = -1;
    private Vector3 currentCommandDestination;

    private float calculationElapsedTime = 0f;
    [SerializeField] private float calculationInterval; // Delay, so that calculations on Tension arent done on each frame. 

    [Header("Current Task Priority")]
    public States states;
    public enum States
    {
        Patrol,
        Chase,
        Listen,
        ExecuteOrder,
        ExecuteHPOrder
    }

    //PatrolPoint Locations
    private Rooms furthestRoom;
    private Room[] rooms;
    private Room[] closestRoom;
    

    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rooms = FindObjectsOfType<Room>();

        closestRoom = new Room[3];


        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove +=  OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;

        FindClosestRoom();
    }

    // Update is called once per frame
    void Update()
    {
        StateHandler();
    }

    //--------
    // STATES
    //--------

    private void StateHandler()
    {
        switch (states)
        {
            case States.Patrol:
                Patrol();
                break;

            case States.Chase:
                //comment
                break;

            case States.Listen: 
                
                break;

            case States.ExecuteOrder:
                if(agent.remainingDistance <= 1f)
                {
                    states = States.Patrol;
                }
                break;

            case States.ExecuteHPOrder:
                if (agent.remainingDistance <= 1f)
                {
                    states = States.Patrol;
                }
                break;
        }
    }



    private void Patrol()
    {
        if (Time.time - calculationElapsedTime >= calculationInterval)
        {
                // Check if the agent has reached the current patrol point
            if (currentRandomIndex != -1 && agent.remainingDistance <= 3.0f)
            {
                FindClosestRoom();
                // If the agent has reached the current patrol point, mark it as checked
                closestRoom[0].patrolPoint[currentRandomIndex].ToggleCheckStatus();

                // Set currentRandomIndex to -1 to indicate that a new index needs to be selected
                currentRandomIndex = -1;
            }
            else if (currentRandomIndex == -1)
            {
                // If currentRandomIndex is -1, it means it's the first patrol point or a new index needs to be selected
                // Get a new random index
                currentRandomIndex = GetRandomUncheckedPointIndex(0);
                if(currentRandomIndex != -1)
                {
                    agent.SetDestination(closestRoom[0].patrolPoint[currentRandomIndex].transform.position);
                }
                // Set destination to the current patrol point
                
            }
            calculationElapsedTime = Time.time;
        }
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
        states = States.ExecuteOrder;

    }

    private void OnHighPriorityCommandToMove(Vector3 target)
    {
        agent.SetDestination(targetPos.position);
        states = States.ExecuteOrder;
    }


    //---------------------
    //PATROL POINT MANAGING
    //---------------------

    private void FindClosestRoom()
    {
        furthestRoom = FindObjectOfType<Rooms>();
        float closestDistance = Mathf.Infinity;

        Room roomInstance = furthestRoom.GetFurthest(transform.position);
        closestRoom[0] = roomInstance;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestRoom[2] = closestRoom[1];
                closestRoom[1] = closestRoom[0];
                closestRoom[0] = room;
            }
        }
    }



    private int GetRandomUncheckedPointIndex(int index)
    {
        int randomIndex = 0;
        int bugFix = 0;
        do
        {
            randomIndex = UnityEngine.Random.Range(0, closestRoom[index].patrolPoint.Length);
            bugFix += 1;
            
        } while (closestRoom[index].patrolPoint[randomIndex].GetComponent<PatrolPoints>().isChecked && bugFix < closestRoom[index].patrolPoint.Length);
        Debug.Log($"RandomIndex {(randomIndex)}");

        return randomIndex;
    }

    private void PushRoomArray()
    {
        closestRoom[0] = rooms[0];
        for(int i = 1; i < closestRoom[0].patrolPoint.Length; i++)
        {
            rooms[i - 1] = rooms[i];
        }
        rooms[rooms.Length - 1] = closestRoom[0];

        if (!CheckPatrolPoint())
        {
            PushRoomArray();
        }
    }

    private bool CheckPatrolPoint()
    {
        bool anyFalse = false;

        for (int i = 0; i < closestRoom[0].patrolPoint.Length; i++)
        {
            if (!closestRoom[0].patrolPoint[i].GetComponent<PatrolPoints>().isChecked)
                anyFalse = true;
        }

        return anyFalse;
    }
}

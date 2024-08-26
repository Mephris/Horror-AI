using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

public class Director : MonoBehaviour
{
    // Tension is a variable which posseses current state of atmosphere 
    [Header("Tension Meter")]
    [Range(0, 100)]
    public float tension;

    private float calculationElapsedTime = 0f;
    [Range(1, 3)]
    [SerializeField]private float calculationTime;
    public static float calculationInterval; // Delay, so that calculations on Tension arent done on each frame. 


    //We save player location to be able to find the locations which we will give Hunter AI
    //while obscuring the player precise location
    [Header("Player Information")]
    [SerializeField] private Transform player;
    //[SerializeField] private float pathfindingDelay = 20.0f;

    [Header("Enemy Information")]
    public Transform hunter;
    public NavMeshAgent hunterAgent;

    //We will use seperate NavMeshAgent in order to create a position which hunter will be able to take.
    private Transform Endpoint;
    private Vector3 EndpointPos;

    private Rooms roomToTarget ;

    //Command time interval so that the command isnt sent out constantly, you can say its an interval in which every x seconds a command is sent to hunter

    //Finite State machine which changes according to the current "tension"
    [Header("Current State/Task")]
    public DirectorStates CurrentState;
    [SerializeField]private DirectorStates PreviousState;
    public enum DirectorStates
    {
        HighPriorityIncreaseTension,
        HighPriorityDecreaseTension,
        IncreaseTension,
        DecreaseTension,
        Observe
    }

    // Start is called before the first frame update
    void Start()
    {
        //Subscribing to events 
        Actions.PlayerCanSeeHunter += OnPlayerCanSeeHunter; // sub do wydarzenia PlayerCanSeeHunter
        Actions.HunterCanSeePlayer += OnHunterCanSeePlayer; // sub do wydarzenia HunterCanSeePlayer

        calculationInterval = calculationTime; // Setting up Calculation interval

        Rooms rooms = FindObjectOfType<Rooms>();
        roomToTarget = rooms;
    }



    // Update is called once per frame
    void Update()
    {
        
        TensionCalculation(); // Cykliczna zmiana zmiennej tension
        StateHandler(); // zmiana stanu maszymy stanowej Command 

    }

    private void FixedUpdate()
    {
        
    }

    private void Awake()
    {
        // ENDPOINT reference which we will manipulate 
        GameObject EndPoint = GameObject.Find("EndPoint");
        Endpoint = EndPoint.GetComponent<Transform>();

        GameObject HunterNavMesh = GameObject.Find("Hunter");
        hunterAgent = HunterNavMesh.GetComponent<NavMeshAgent>();

        
    }

    private void StateHandler()
    {
        // Changing Hunter Command which will be sent to the Hunter AI
        CurrentState = tension < 15 ? DirectorStates.HighPriorityIncreaseTension :
                       tension > 85 ? DirectorStates.HighPriorityDecreaseTension :
                       tension < 35 ? DirectorStates.IncreaseTension :
                       tension > 70 ? DirectorStates.DecreaseTension :
                       DirectorStates.Observe;

        //Debug.Log(FindObjectOfType<Rooms>());
        // Depending on state, send the chosen command to Hunter AI
        switch (CurrentState)
        {
            //Send Hunter to
            case DirectorStates.IncreaseTension:
                //to closest room
                EndpointPos = FindObjectOfType<Rooms>().ClosestRoom();
                break;

            case DirectorStates.DecreaseTension:
                //entrance to the furthest viable room
                EndpointPos = FindObjectOfType<Rooms>().PosFarFromPlayer();
                break;

            case DirectorStates.HighPriorityDecreaseTension:
                //furthest viable room
                EndpointPos = FindObjectOfType<Rooms>().FurthestRoom();
                break;

            case DirectorStates.HighPriorityIncreaseTension:
                //last turn between player and enemy in pathfinding
                Vector3 PosVec3 = FindObjectOfType<Rooms>().PosNearPlayer();
                EndpointPos = PosVec3;
                break;

            case DirectorStates.Observe:


                break;

        }
        GiveCommandToMove();
    }

    private void GiveCommandToMove()
    {
        if (PreviousState != CurrentState || CurrentState == DirectorStates.HighPriorityDecreaseTension)
        {
            if(CurrentState != DirectorStates.Observe)
            {
                Endpoint.transform.position = EndpointPos;

                if (CurrentState == DirectorStates.HighPriorityIncreaseTension || CurrentState == DirectorStates.HighPriorityDecreaseTension)
                {
                    Actions.HighPriorityCommandToMove(Endpoint.position);
                }
                else
                {
                    Actions.CommandToMove(Endpoint.position);
                }
            }
            PreviousState = CurrentState;
        }
    }
    
    //---------------------
    // TENSION CYCLE
    //---------------------
    private void TensionCalculation()
    {
        if (Time.time - calculationElapsedTime >= calculationInterval)
        {
            tension += Vector3.Distance(player.position, hunter.position) < 12f ? 1 :
                       Vector3.Distance(player.position, hunter.position) > 12f ? -1 : 0;

            
            calculationElapsedTime = Time.time;
        }
    }
    //---------------------------
    // TENSION CHANGING EVENTS
    //---------------------------
    private void OnPlayerCanSeeHunter(bool obj)
    {
        if (obj == true)
            tension += 1;
    }

    private void OnHunterCanSeePlayer(bool obj, Vector3 lastPlayerLocation)
    {
        if (obj == true)
            tension += 1;
    }

}

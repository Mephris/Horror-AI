using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

public class Director : MonoBehaviour
{
    // Tension is a variable which will decide which command will be issued to the Hunter AI
    [Header("Tension Meter")]
    public float tension;

    private float calculationElapsedTime = 0f;
    [SerializeField]private float calculationTime;
    public static float calculationInterval; // Delay, so that calculations on Tension arent done on each frame. 


    //We save player location to be able to find the locations which we will give Hunter AI
    //while obscuring the player precise location
    [Header("Player Information")]
    [SerializeField] private Transform player;
    //[SerializeField] private float pathfindingDelay = 20.0f;

    [Header("Enemy Information")]
    [SerializeField] private Transform hunter;
    private NavMeshAgent hunterAgent;

    //We will use seperate NavMeshAgent in order to create a position which hunter will be able to take.
    private Transform Endpoint;
    private Vector3 EndpointPos;

    private Rooms roomToTarget ;

    //Command time interval so that the command isnt sent out constantly, you can say its an interval in which every x seconds a command is sent to hunter

    //Hunter commands is a simple state machine that we will use, States will switch according to tension Meter
    [Header("Current State/Task")]
    public Commands CurrentState;
    private Commands PreviousState;
    public enum Commands
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
        Actions.PlayerCanSeeHunter += OnPlayerCanSeeHunter;
        Actions.HunterCanSeePlayer += OnHunterCanSeePlayer;

        calculationInterval = calculationTime;

        Rooms rooms = FindObjectOfType<Rooms>();
        roomToTarget = rooms;
    }



    // Update is called once per frame
    void Update()
    {
        TensionCalculation();
        StateHandler();

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
        CurrentState = tension < 10 ? Commands.HighPriorityIncreaseTension :
                       tension > 90 ? Commands.HighPriorityDecreaseTension :
                       tension < 30 ? Commands.IncreaseTension :
                       tension > 60 ? Commands.DecreaseTension :
                       Commands.Observe;

        

        // Depending on state, send the  chosen command to Hunter AI
        switch (CurrentState)
        {
            case Commands.IncreaseTension:

                SecondClosestRoom();
                break;

            case Commands.DecreaseTension:

                PosFarFromPlayer();
                break;

            case Commands.HighPriorityDecreaseTension:

                FurthestRoom();
                break;

            case Commands.HighPriorityIncreaseTension:

                PosNearPlayer();
                break;

            case Commands.Observe:

                //Maybe some little events? Tweaks to Hunter Perception?
                
                break;

            default:

                //CHILLLLLLLLL DO NOTHING MAAAAAN
                break;

        }
        GiveCommandToMove();
    }

    private void GiveCommandToMove()
    {
        if (PreviousState != CurrentState)
        {
            Endpoint.transform.position = EndpointPos;
            Actions.CommandToMove(Endpoint.position);
            PreviousState = CurrentState;
        }
    }

    //---------------------
    //TENSION CALCULATIONS
    //---------------------
    private void TensionCalculation()
    {
        if (Time.time - calculationElapsedTime >= calculationInterval)
        {
            tension += Vector3.Distance(player.position, hunter.position) < 10f ? 1 :
                       Vector3.Distance(player.position, hunter.position) > 12f ? -1 : 0;

            calculationElapsedTime = Time.time;
        }
    }
    //---------------------------
    //EVENTS TRIGGERED BY ACTIONS
    //---------------------------
    private void OnPlayerCanSeeHunter(bool obj)
    {
        if (obj == true)
            tension += 1;
    }

    private void OnHunterCanSeePlayer(bool obj)
    {
        if (obj == true)
            tension += 2;
    }

    //------------------------------------------------
    // LOCATION CALCULATIONS \ WHERE SHOULD HUNTER GO
    //------------------------------------------------
    private void PosNearPlayer()
    {

        NavMeshPath path = new NavMeshPath();
        if (hunterAgent.CalculatePath(player.position, path))
        {

            if (path.corners.Length > 1) // Ensure there is more than one corner
            {
                // The second-to-last corner is the new endpoint
                EndpointPos = path.corners[path.corners.Length - 2];
            }
            else if (path.corners.Length == 1) // If there's only one corner, use it as the endpoint
            {
                EndpointPos = path.corners[0];
            }
        }
    }
    private void PosFarFromPlayer()
    {
        Room targetRoom = this.roomToTarget.MostCostMovement(player.position);
        NavMeshPath path = new NavMeshPath();
        if (hunterAgent.CalculatePath(targetRoom.transform.position, path))
        {

            if (path.corners.Length > 1) // Ensure there is more than one corner
            {
                // The second-to-last corner is the new endpoint
                EndpointPos = path.corners[path.corners.Length - 2];
            }
            else if (path.corners.Length == 1) // If there's only one corner, use it as the endpoint
            {
                EndpointPos = path.corners[0];
            }
        }
    }
    private void FurthestRoom()
    {
        Room targetRoom = this.roomToTarget.MostCostMovement(player.position);
        EndpointPos = targetRoom.transform.position;
    }

    private void SecondClosestRoom()
    {
        Room targetRoom = this.roomToTarget.LeastCostMovement(hunter.position, player.position);
        EndpointPos = targetRoom.transform.position;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;

public class Director : MonoBehaviour
{
    // Tension is a variable which posseses current state of atmosphere 
    [Header("Tension Meter")]
    [Range(0, 100)]
    public double tension;

    private float calculationElapsedTime = 0f;
    [Range(1, 3)]
    [SerializeField] private float calculationTime;
    public static float calculationInterval; // Delay, so that calculations on Tension arent done on each frame. 

    [Header("Tension Decay")]
    [Tooltip("Rate at which tension decays per second when the Hunter is not actively seeing the player.")]
    [Range(0.1f, 10f)] // A value like 0.5f means 50 tension points decay every 100 seconds
    public float tensionDecayRate = 1.0f;
    // Let's assume tension is up to 100, so 1.0f means it takes 100 seconds to fully decay from max.

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
    private Rooms roomToTarget;

    //Finite State machine which changes according to the current "tension"
    [Header("Current State/Task")]
    public DirectorStates CurrentState;
    [SerializeField] private DirectorStates PreviousState;
    public enum DirectorStates
    {
        HighPriorityIncreaseTension,
        HighPriorityDecreaseTension,
        IncreaseTension,
        DecreaseTension,
        Observe
    }

    [Header("Rooms Reference")]
    private Rooms roomsController;

    [Header("Hunter Command Logic")]
    [Tooltip("Time player must be seen by Hunter to trigger a High Priority Command (memory tag).")]
    [SerializeField] private float highTensionThresholdTime = 15f;
    private float highTensionTimeElapsed = 0f;
    private bool isHighPriorityCommandSent = false;

    // Start is called before the first frame update
    void Start()
    {
        Actions.HunterCanSeePlayer += OnHunterCanSeePlayer;
        Actions.PlayerCanSeeHunter += OnPlayerCanSeeHunter;

        // Initialize Rooms reference (Assuming Rooms is on a GameObject in the scene)
        roomsController = FindObjectOfType<Rooms>();

        // Existing calculation setup
        calculationInterval = calculationTime;
    }



    // Update is called once per frame
    void Update()
    {

        TensionCalculation(); // Cykliczna zmiana zmiennej tension

        if (tension > 0)
        {
            tension -= tensionDecayRate * Time.deltaTime;
            tension = Math.Max(0, tension); // Ensure tension never goes below 0
        }

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
                // 1. Check that our references are valid before we use them
                if (hunterAgent == null) Debug.LogError("Director's hunterAgent is NOT assigned in the Inspector!");
                if (player == null) Debug.LogError("Director's player is NOT assigned in the Inspector!");

                // 2. Pass the agent and player.position to the method
                Vector3 PosVec3 = FindObjectOfType<Rooms>().PosNearPlayer(hunterAgent, player.position);

                EndpointPos = PosVec3;
                break;

            case DirectorStates.Observe:


                break;

        }
        UpdateTensionState();
    }

    private void UpdateTensionState()
    {
        // [Your existing logic for TensionCalculation is assumed to be called regularly]

        if (PreviousState != CurrentState || CurrentState == DirectorStates.HighPriorityDecreaseTension)
        {
            if (CurrentState != PreviousState)
            {
                // ... [Your existing logic for Low, Medium state transitions] ...

                // --- CORRECTED STANDARD COMMAND LOGIC ---
                if (roomsController != null && hunterAgent != null && player != null)
                {
                    // Pass the Hunter's agent and the Player's position as arguments.
                    Vector3 commandTarget = roomsController.PosNearPlayer(hunterAgent, player.position);

                    // Fire the standard command event (minor probability increase in Hunter memory)
                    Actions.CommandToMove?.Invoke(commandTarget);
                    Debug.Log($"DIRECTOR: Standard Command (Memory Tag) sent to area near player based on State Change. Target: {commandTarget}");
                }
                // --- END CORRECTED LOGIC ---
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
            tension += 0.5;

        // !!! Change how High Priority Increase Tension works !!!
        // Change it to a timer based on how long is Hunter seen by Player. If seen for 15 seconds, then give high priority order. 
    }

    // --- In Director.cs (Modify this method) ---

    // --- In Director.cs (Update OnHunterCanSeePlayer method) ---

    private void OnHunterCanSeePlayer(bool obj, Vector3 lastPlayerLocation)
    {
        if (obj == true)
        {
            tension += 1;

            // 1. Time-tracking for sustained sight
            highTensionTimeElapsed += Time.deltaTime;

            // 2. Trigger High Priority Command (Memory Tag)
            // If sustained sight (e.g., 15s) and the command hasn't been sent yet
            if (highTensionTimeElapsed >= highTensionThresholdTime && !isHighPriorityCommandSent)
            {
                // Send the last seen player location to the Hunter's memory (strong memory tag)
                Actions.HighPriorityCommandToMove?.Invoke(lastPlayerLocation); // <-- FIRES THE EVENT
                isHighPriorityCommandSent = true;
                Debug.Log("DIRECTOR: High Priority Command (Memory Tag) sent due to sustained Hunter sight.");
            }
        }
        else // Hunter lost sight of the player
        {
            // Reset the tension timer and command flag
            highTensionTimeElapsed = 0f;
            isHighPriorityCommandSent = false;
        }
    }

}

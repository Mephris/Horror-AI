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

    // 1. Add this new field near your other Header fields
    [Header("Tension Decay")]
    [Tooltip("Amount of tension to decrease per calculation interval (e.g., every 1-3 seconds) when the Hunter is far.")]
    [SerializeField] private float tensionDistanceDecayAmount = 1f;


    // High Tension Trigger
    [Header("High Priority Command")]
    [Tooltip("Time Hunter must see Player to trigger a High Priority Command/Memory Tag.")]
    [SerializeField] private float highTensionThresholdTime = 15.0f;
    private float highTensionTimeElapsed = 0f;
    private bool isHighPriorityCommandSent = false;


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

    //Director states
    public enum DirectorStates
    {
        Idle,
        LowTension,
        MediumTension,
        HighTension,
        ExtremeTension
    }

    private void Awake()
    {
        calculationInterval = calculationTime;
        roomToTarget = FindObjectOfType<Rooms>();
    }

    private void Start()
    {
        // Subscribe to events
        Actions.PlayerCanSeeHunter += OnPlayerCanSeeHunter;
        Actions.HunterCanSeePlayer += OnHunterCanSeePlayer;
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        Actions.PlayerCanSeeHunter -= OnPlayerCanSeeHunter;
        Actions.HunterCanSeePlayer -= OnHunterCanSeePlayer;
    }

    private void Update()
    {
        TensionCalculation();
        StateChange();
    }

    // --- Tension Handlers (Updated for FOV) ---

    private void OnPlayerCanSeeHunter(bool obj)
    {
        if (obj == true)
            tension += 0.5;
    }


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
            // Do not decrease tension here, it is already handled in TensionCalculation().

            // Reset the tension timer and command flag
            highTensionTimeElapsed = 0f;
            isHighPriorityCommandSent = false;
        }
    }


    private void TensionCalculation()
    {
        calculationElapsedTime += Time.deltaTime;
        if (calculationElapsedTime >= calculationTime)
        {
            calculationElapsedTime = 0f;

            // Get distance between player and hunter
            float distance = Vector3.Distance(player.position, hunter.position);

            // Distance-based Tension Decay
            if (distance > 30) // Decay if Hunter is far from the player
            {
                tension = Math.Max(0, tension - tensionDistanceDecayAmount);
            }

            // Clamp tension to ensure it stays between 0 and 100
            tension = Mathf.Clamp((float)tension, 0f, 100f);
        }
    }

    private void StateChange()
    {
        // State change logic based on tension levels (TBD)
    }

    // --- Director Command Generation (TBD) ---

    // ... (rest of the functions like PosCloseToPlayer, PosFarFromPlayer, etc. remain the same) ...

    public Vector3 PosCloseToPlayer()
    {
        Room targetRoom = roomToTarget.ClosestRoomComponent();
        NavMeshPath path = new NavMeshPath();
        if (FindObjectOfType<Director>().hunterAgent.CalculatePath(targetRoom.transform.position, path))
        {
            // If the path has multiple corners, we target the second to last corner
            if (path.corners.Length > 1)
            {
                // The second-to-last corner is the new endpoint
                return path.corners[path.corners.Length - 2];
            }
            // If there's only one corner, use it as the endpoint
            else if (path.corners.Length == 1)
            {
                return path.corners[0];
            }
        }
        return Vector3.zero;
    }
    public Vector3 PosFarFromPlayer()
    {
        Room targetRoom = roomToTarget.MostCostMovement(player.transform.position);
        NavMeshPath path = new NavMeshPath();
        if (FindObjectOfType<Director>().hunterAgent.CalculatePath(targetRoom.transform.position, path))
        {

            if (path.corners.Length > 1) // Ensure there is more than one corner
            {
                // The second-to-last corner is the new endpoint
                return path.corners[path.corners.Length - 2];
            }
            else if (path.corners.Length == 1) // If there's only one corner, use it as the endpoint
            {
                return path.corners[0];
            }
        }

        return Vector3.zero;
    }
    public Vector3 FurthestRoom()
    {
        return roomToTarget.MostCostMovement(player.transform.position).transform.position;
    }

    public Vector3 ClosestRoom()
    {
        return roomToTarget.ClosestRoomComponent().transform.position;
    }
}
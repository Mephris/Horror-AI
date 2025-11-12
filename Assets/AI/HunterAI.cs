using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
// Ensure you have this using statement for the Behavior Tree Node types
using static Node;


[System.Serializable]
public class HunterPatrolMemory
{
    public Transform patrolpointTransform;
    // Tracks the last time the hunter completed an investigation here
    public float lastPatrolTime = 0f;

    // THE CORE PROBABILITY SCORE (0.0=Clear to 1.0=Likely Here)
    public float playerProbability = 0f;

    // Discrete memory tags for specific events
    public bool hasHeardNoise = false;
    public bool hasSeenDisturbance = false;
    public bool hasDirectorTip = false;

    // Quick Check for BT to decide if this point is a high priority detour
    public bool IsWorthyOfInvestigation => hasHeardNoise || hasSeenDisturbance || hasDirectorTip || playerProbability > 0.5f;

    // The final value the BT will use to compare points.
    public float calculatedPriorityScore = 0f;
}


public class HunterAI : MonoBehaviour
{
    // --- Behavior Tree Fields ---
    private Node rootNode;
    private HunterBehaviorNodes btContext;

    [Header("Behavior State Debug")]
    public string currentBTState = "IDLE (Not Initialized)";

    // --- Components ---
    private NavMeshAgent agent;
    // NEW: Reference to the FieldOfView component
    public FieldOfView fieldOfView;

    // --- Navigation & Targets (Made public for HunterBehaviorNodes) ---
    public Transform currentPatrolTarget = null;
    // Last known or commanded position (must be unparented transform!)
    public Transform targetPos;
    [Tooltip("The path cost threshold for the Director to consider a patrol point reachable.")]
    [SerializeField] private float directorPathCostThreshold = 40f;


    // --- PatrolPoint & Room Data ---
    // Stores memory for every patrol point in the scene
    public Dictionary<Transform, HunterPatrolMemory> patrolPointData = new Dictionary<Transform, HunterPatrolMemory>();


    // --- Investigation Timer (Manages the 7-second stop at last-seen location) ---
    [Header("Investigation Timer")]
    [Tooltip("How long the Hunter stops to investigate a high-priority point or last-seen player location.")]
    public float investigationDuration = 7.0f;
    [HideInInspector] public float investigationTimeElapsed = 0f;
    [HideInInspector] private bool isInvestigating = false;
    private Transform pointBeingInvestigated = null;


    private void Awake()
    {
        // Get Components
        agent = GetComponent<NavMeshAgent>();
        fieldOfView = GetComponent<FieldOfView>(); // GET FOV COMPONENT

        // Setup BT context and root
        btContext = new HunterBehaviorNodes(this, agent);
        rootNode = btContext.SetupBehaviorTree();

        // Initialize memory for all patrol points in the scene
        InitializePatrolPointMemory();
    }

    private void OnEnable()
    {
        // Subscribe to Director Events
        Actions.CommandToMove += OnDirectorCommandToMove;
        Actions.HighPriorityCommandToMove += OnDirectorHighPriorityCommandToMove;
    }

    private void OnDisable()
    {
        // Unsubscribe from Director Events
        Actions.CommandToMove -= OnDirectorCommandToMove;
        Actions.HighPriorityCommandToMove -= OnDirectorHighPriorityCommandToMove;
    }


    void Update()
    {
        TickBT();

        // Update the elapsed time for investigation *outside* the BT node
        if (isInvestigating)
        {
            investigationTimeElapsed += Time.deltaTime;
        }

        // Always check if the FOV is valid
        if (fieldOfView == null)
        {
            Debug.LogError("HunterAI requires a FieldOfView component to be attached!");
        }
    }

    // --- Behavior Tree Logic ---

    public void TickBT()
    {
        // Evaluate the root node
        rootNode.Evaluate();
    }

    // --- Event Handlers ---

    private void OnDirectorCommandToMove(Vector3 targetLocation)
    {
        // Call the memory update with a low probability increase and no DirectorTip tag
        UpdatePatrolPointMemory(targetLocation, 0.15f, false);
    }

    private void OnDirectorHighPriorityCommandToMove(Vector3 targetLocation)
    {
        // Call the memory update with a high probability increase and the DirectorTip tag
        UpdatePatrolPointMemory(targetLocation, 0.40f, true);
    }

    // --- Memory and Utility ---

    private void InitializePatrolPointMemory()
    {
        // Find all patrol points (ensure they are tagged correctly or use a Manager)
        PatrolPoints[] allPatrolPoints = FindObjectsOfType<PatrolPoints>();

        foreach (var pp in allPatrolPoints)
        {
            // Initialize memory for each point
            HunterPatrolMemory newMemory = new HunterPatrolMemory
            {
                patrolpointTransform = pp.transform,
                lastPatrolTime = Time.time // Initialize as 'just visited' to prevent all being high priority initially
            };
            patrolPointData.Add(pp.transform, newMemory);
        }
    }

    /// <summary>
    /// Finds all patrol points that are reachable from the given source position 
    /// and updates their memory based on the director's input.
    /// </summary>
    /// <param name="sourceLocation">The location (e.g., last known player position) to search from.</param>
    /// <param name="probabilityIncrease">How much to increase the probability score (0.0 to 1.0).</param>
    /// <param name="setDirectorTip">Whether to set the hasDirectorTip flag.</param>
    public void UpdatePatrolPointMemory(Vector3 sourceLocation, float probabilityIncrease, bool setDirectorTip)
    {
        NavMeshPath path = new NavMeshPath();
        List<Transform> pointsToUpdate = new List<Transform>();

        // 1. Find reachable points
        foreach (var kvp in patrolPointData)
        {
            Transform pointTransform = kvp.Key;

            // Check path and reachability
            if (agent.CalculatePath(sourceLocation, path))
            {
                float cost = CalculatePathCost(path);
                if (cost != float.MaxValue && cost <= directorPathCostThreshold)
                {
                    pointsToUpdate.Add(pointTransform);
                }
            }
        }

        // 2. Update memory for reachable points
        foreach (Transform pointTransform in pointsToUpdate)
        {
            // Retrieve, modify, and store the struct back
            HunterPatrolMemory memory = patrolPointData[pointTransform];

            // Increase probability, clamping it between 0 and 1
            memory.playerProbability = Mathf.Clamp01(memory.playerProbability + probabilityIncrease);

            // Set the Director Tip flag (only for High Priority Commands)
            if (setDirectorTip)
            {
                memory.hasDirectorTip = true;
            }

            // Update the dictionary with the modified struct
            patrolPointData[pointTransform] = memory;
        }

        if (pointsToUpdate.Count > 0)
        {
            Debug.Log($"Director Command: Updated memory for **{pointsToUpdate.Count}** reachable patrol point(s). Prob Inc: {probabilityIncrease:F2}. Tip: {setDirectorTip}");
        }
        else
        {
            Debug.LogWarning("Director Command: No reachable patrol points found within the path cost threshold.");
        }
    }

    // This helper is needed because NavMeshPath doesn't give a simple cost property
    private float CalculatePathCost(NavMeshPath path)
    {
        float cost = 0f;
        for (int i = 0; i < path.corners.Length - 1; i++)
        {
            cost += Vector3.Distance(path.corners[i], path.corners[i + 1]);
        }
        // If the path is invalid, return a very high value
        if (path.status != NavMeshPathStatus.PathComplete)
        {
            return float.MaxValue;
        }
        return cost;
    }


    // --- Investigation Timer Management ---

    /// <summary>
    /// Starts the investigation process at the given point.
    /// </summary>
    public void StartInvestigation(Transform point)
    {
        isInvestigating = true;
        investigationTimeElapsed = 0f;
        pointBeingInvestigated = point;
        agent.isStopped = true; // Stop the Hunter when investigation begins
        Debug.Log($"Starting {investigationDuration}s investigation at {point.name}");
    }

    /// <summary>
    /// Updates the investigation timer and returns the state (RUNNING or SUCCESS).
    /// </summary>
    public NodeState UpdateInvestigationTimer()
    {
        if (!isInvestigating)
        {
            // Should not happen if called correctly by the BT
            return NodeState.SUCCESS;
        }

        if (investigationTimeElapsed >= investigationDuration)
        {
            // Investigation Complete
            isInvestigating = false;
            if (pointBeingInvestigated != null)
            {
                // Mark the memory as checked
                HunterPatrolMemory memory = patrolPointData[pointBeingInvestigated];
                memory.lastPatrolTime = Time.time;
                memory.playerProbability = 0f; // Clear probability upon successful investigation
                memory.hasDirectorTip = false; // Clear director tip
                patrolPointData[pointBeingInvestigated] = memory;

                // Also update the PatrolPoints component itself
                pointBeingInvestigated.GetComponent<PatrolPoints>()?.ToggleCheckStatus();
            }
            pointBeingInvestigated = null; // Clear the point reference
            agent.isStopped = false; // Allow movement to resume
            return NodeState.SUCCESS;
        }

        // Investigation Running
        return NodeState.RUNNING;
    }

    // -- GIZMO's FOR DEBUGGING --

    // Helper method for gizmos visualization
    public float GetProbabilityScore(Transform patrolPoint)
    {
        if (patrolPointData.TryGetValue(patrolPoint, out HunterPatrolMemory memory))
        {
            return memory.playerProbability;
        }
        return 0f; // Should not happen if data is initialized correctly
    }

    private void OnDrawGizmos()
    {
        if (targetPos != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(targetPos.position, 0.5f);
            Gizmos.DrawLine(transform.position, targetPos.position);
        }
    }
}
using UnityEngine;
using UnityEngine.AI;


public class Hunter_Basic : MonoBehaviour
{

    [SerializeField] private Transform targetPos;

    private NavMeshAgent agent;

    private bool isMoving = false;
    private bool isNotChasing = true;

    private float calculationInterval;
    private float calculationElapsedTime = 0f;

    [Header("Current Task Priority")]
    public States states;
    private States previousState;
    public enum States
    {
        Patrol,
        SwitchRoom,
        Chase,
        Listen,
        ExecuteOrder,
        ExecuteHPOrder
    }

    //PatrolPoint Locations
    private Room[] rooms;
    private Room closestRoom;


    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        rooms = FindObjectsOfType<Room>();


        ClosestRoom();

        Actions.HighPriorityCommandToMove += OnHighPriorityCommandToMove;
        Actions.CommandToMove += OnCommandToMove;
        Actions.HunterCanSeePlayer += OnSeePlayer;


        calculationInterval = Director.calculationInterval / 5.0f;



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
        if (Time.time - calculationElapsedTime >= calculationInterval)
        {
            switch (states)
            {
                case States.Patrol:
                    agent.speed = 2.0f;
                    Patrol();
                    break;

                case States.SwitchRoom:
                    agent.speed = 3.0f;
                    if (!isMoving)
                    {
                        agent.SetDestination(ClosestRoom().transform.position);
                        isMoving = true;
                    }
                    if (agent.remainingDistance < 1.0f)
                    {
                        isMoving = false;
                        states = States.Patrol;
                    }

                    break;

                case States.Chase:
                    agent.speed = 3.5f;
                    break;

                case States.Listen:

                    break;

                case States.ExecuteOrder:

                    agent.speed = 3.0f;
                    isMoving = true;
                    previousState = states;
                    if (agent.remainingDistance <= 3.0f)
                    {
                        isMoving = false;
                        closestRoom = CurrentRoom();
                        states = States.Patrol;
                    }
                    break;

                case States.ExecuteHPOrder:

                    agent.speed = 3.5f;
                    isMoving = true;
                    previousState = states;
                    if (!isNotChasing)
                    {
                        Actions.HunterCanSeePlayer -= OnSeePlayer;
                    }
                    if (agent.remainingDistance <= 3.0f)
                    {
                        Actions.HunterCanSeePlayer += OnSeePlayer;
                        isMoving = false;
                        closestRoom = CurrentRoom();
                        states = States.Patrol;

                    }
                    break;
            }
            calculationElapsedTime = Time.time;
        }
    }


    private Room ClosestRoom()
    {
        Room currentRoom = null;
        float closestDistance = Mathf.Infinity;
        Room closestRoomCandidate = null;
        float closestDistanceCandidate = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            if (distance < closestDistance && !AllPointsChecked(room))
            {
                closestDistanceCandidate = closestDistance;
                closestRoomCandidate = currentRoom;

                closestDistance = distance;
                currentRoom = room;
            }
            else if (distance < closestDistanceCandidate && !AllPointsChecked(room))
            {
                closestDistanceCandidate = distance;
                closestRoomCandidate = room;
            }
        }
        closestRoom = currentRoom;
        return closestRoomCandidate;
    }

    private Room CurrentRoom()
    {
        Room currentRoom = null;
        float smallestDistance = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            if (distance < smallestDistance && !AllPointsChecked(room))
            {
                smallestDistance = distance;
                currentRoom = room;
            }
        }

        return currentRoom;
    }

    private void Patrol()
    {
        if (!isMoving)
        {
            foreach (PatrolPoints point in closestRoom.patrolPoint)
            {
                if (!point.isChecked)
                {
                    // Set the flag to indicate that the agent is moving to a patrol point
                    isMoving = true;

                    // Set the destination to the patrol point
                    agent.SetDestination(point.transform.position);

                    // Toggle the check status of the patrol point
                    point.ToggleCheckStatus();

                    // Exit the loop to allow the agent to reach its destination
                    break;
                }
                else
                {
                    if (AllPointsChecked(closestRoom) && previousState != States.ExecuteHPOrder)
                        states = States.SwitchRoom;
                }
            }
        }
        else
        {
            // Check if the agent has reached the patrol point
            if (agent.remainingDistance <= 0.1f)
            {
                // Reset the flag once the agent reaches the patrol point
                isMoving = false;
            }
        }
    }

    private void Listen()
    {

    }

    private void Chase()
    {

    }


    //---------------------------
    //EVENTS TRIGGERED BY ACTIONS
    //---------------------------
    private void OnSeePlayer(bool canSeePlayer, Vector3 lastSeenTargetLocation)
    {
        if (states != States.ExecuteHPOrder)
        {
            if (canSeePlayer)
            {
                agent.speed = 4f;
                states = States.Chase;
                agent.SetDestination(lastSeenTargetLocation);
                isMoving = true;
                isNotChasing = false; // Disable CommandToMove while chasing
            }
            else if (!isNotChasing)
            {
                if (Vector3.Distance(transform.position, lastSeenTargetLocation) < 0.3f)
                {
                    agent.speed = 3f;
                    states = States.SwitchRoom;
                    isMoving = false;
                    isNotChasing = true; // Enable CommandToMove when not chasing
                    agent.SetDestination(lastSeenTargetLocation);
                }
            }
            
        }
    }

    private void OnCommandToMove(Vector3 target)
    {
        if (!isNotChasing)
            return;
        ResetRoomPatrolPoints(FindRoom_Command(target));
        agent.SetDestination(targetPos.position);
        states = States.ExecuteOrder;

    }

    private void OnHighPriorityCommandToMove(Vector3 target)
    {
        ResetRoomPatrolPoints(FindRoom_Command(target));
        agent.SetDestination(targetPos.position);
        states = States.ExecuteHPOrder;
    }


    //---------------------
    //PATROL POINT MANAGING
    //---------------------


    private int GetRandomUncheckedPointIndex()
    {
        int randomIndex = 0;
        int bugFix = 0;
        do
        {
            randomIndex = UnityEngine.Random.Range(0, closestRoom.patrolPoint.Length);
            bugFix += 1;

        } while (closestRoom.patrolPoint[randomIndex].GetComponent<PatrolPoints>().isChecked && bugFix < closestRoom.patrolPoint.Length);
        Debug.Log($"RandomIndex {(randomIndex)}");

        if (!AllPointsChecked(closestRoom))
        {
            randomIndex = -1;
        }

        return randomIndex;
    }

    private bool AllPointsChecked(Room room)
    {
        foreach (var point in room.patrolPoint)
        {
            if (!point.GetComponent<PatrolPoints>().isChecked)
            {
                return false;
            }
        }
        return true;
    }
    private Room FindRoom_Command(Vector3 target)
    {
        Room roomNearby = null;
        float closestDistance = Mathf.Infinity;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(transform.position, room.transform.position);
            if (distance < closestDistance && !AllPointsChecked(room))
            {
                closestDistance = distance;
                roomNearby = room;
            }
        }
        return roomNearby;
    }
    private void ResetRoomPatrolPoints(Room room)
    {
        foreach (var point in room.patrolPoint)
        {
            point.ResetCheckStatus();
        }
    }

    //GIZMO DRAWING

}

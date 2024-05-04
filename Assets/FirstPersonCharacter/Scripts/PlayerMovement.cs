using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    // Speed and "halt" force, so that player doesnt slide around the ground
    [Header("Movement")]
    private float moveSpeed;
    public float walkSpeed;
    public float sprintSpeed;
    public float crouchSpeed;

    public float groundDrag;

    // Crouch values
    [Header("Crouching")]
    public float crouchTransitionDuration;
    public float crouchYScale; 
    
    [Header("Keybinds")]
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;

    // Checking if player is on the ground or is in state falling
    [Header("GroundCheck")]
    public float playerHeight;
    public LayerMask whatIsGround;
    [SerializeField] private bool grounded = true;

    public Transform orientation;

    private float startYScale = 1f;
    private Vector3 targetScale;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    Rigidbody rb;

    public MovementState playerState;
    public enum MovementState
    {
        walking,
        sprinting,
        crouching,
        falling
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        // putting player to the ground
        rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);

        targetScale = new Vector3(transform.localScale.x, startYScale, transform.localScale.z);
    }

    private void Update()
    {
        //ground check
        //grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.9f + 0.2f, whatIsGround);

        MyInput();
        SpeedControl();
        StateHandler();

        //if (grounded)
            rb.drag = groundDrag;
        //else
           // rb.drag = 0;


    }

    private void FixedUpdate()
    {
        MovePlayer();

    }

    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        //crouching
        if (Input.GetKeyDown(crouchKey))
        {
            // Toggle crouching state
            playerState = (playerState == MovementState.walking) ? MovementState.crouching : MovementState.walking;

            // Set the target scale based on crouching or standing up
            targetScale.y = (playerState == MovementState.crouching) ? crouchYScale : startYScale;

            // Start the smooth transition
            

        }
        
        if (Input.GetKeyUp(crouchKey))
        {
            // Toggle crouching state
            playerState = (playerState == MovementState.crouching) ? MovementState.walking : MovementState.crouching;

            // Set the target scale based on crouching or standing up
            targetScale.y = (playerState == MovementState.crouching) ? crouchYScale : startYScale;

            // Start the smooth transition
            
        }
        StartCoroutine(SmoothHeightTransition());


    }

    private void SpeedControl()
    {
        Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        // putting a limit on the max speed so it doesn't go above max.
        if (flatVel.magnitude > moveSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * moveSpeed;
            rb.velocity = new Vector3(limitedVel.x, rb.velocity.y, limitedVel.z);
        }
    }

    private void StateHandler()
    {

        if (grounded)
        {
            if (Input.GetKey(crouchKey))
            {
                playerState = MovementState.crouching;
                moveSpeed = crouchSpeed;
            }
            else if (Input.GetKey(sprintKey))
            {
                playerState = MovementState.sprinting;
                moveSpeed = sprintSpeed;
            }
            else
            {
                playerState = MovementState.walking;
                moveSpeed = walkSpeed;
            }
        }
        /*
        else if (!grounded)
        {
            state = MovementState.falling;
            moveSpeed = 0;
        }
        */

    }

    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);

        // If no input, apply opposing force
        if (horizontalInput == 0 && verticalInput == 0)
        {
            float fasterDecelerationForce = 1f;
            Vector3 currentVelocity = rb.velocity;
            Vector3 opposingForce = -currentVelocity.normalized * fasterDecelerationForce;
            rb.AddForce(opposingForce, ForceMode.Acceleration);
        }
    }

    private IEnumerator SmoothHeightTransition()
    {
        float elapsedTime = 0f;
        Vector3 initialScale = transform.localScale;

        while (elapsedTime < crouchTransitionDuration)
        {
            // Interpolate between initial scale and target scale
            float t = elapsedTime / crouchTransitionDuration;
            transform.localScale = Vector3.Lerp(initialScale, targetScale, t);

            // Update elapsed time
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // Ensure the final scale matches the target scale exactly
        transform.localScale = targetScale;
    }


    

}

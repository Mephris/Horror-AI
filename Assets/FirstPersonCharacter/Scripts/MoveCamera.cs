using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UIElements;
using static PlayerMovement;


public class MoveCamera : MonoBehaviour
{

    [Header("ScreenShake")]
    public float shakeIntensity; // Adjust the intensity of the shake
    public float shakeSpeed;     // Adjust the speed of the shake

    // The values on which we will be calculating the shake speed and intensity during states of crouching and sprinting
    private float shakeSpeedUsed;
    private float shakeIntensityUsed;

    private float timeElapsed = 1f;
    private Rigidbody PlayerRigidBody;
    public MovementState cameraState;
   
    public Transform cameraPosition;



    private void Start()
    {
        PlayerRigidBody = GameObject.Find("Player").GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    private void Update()
    {
        transform.position = cameraPosition.position;
        ScreenShake();
    }
    private void ScreenShake()
    {
        // Calculate the target values based on the movement state
        float targetShakeSpeed;
        float targetShakeIntensity;

        //Creating min and max values, clamps of where the camera can go.
        Vector3 PlayerPosition = GameObject.Find("Player").transform.position;
        float minYLimit = PlayerPosition.y;
        float maxYLimit = PlayerPosition.y;


        switch (cameraState)
        {
            case MovementState.crouching:

                targetShakeSpeed = shakeSpeed / 2.0f;
                targetShakeIntensity = shakeIntensity / 2.0f;
                minYLimit = PlayerPosition.y + 0.48f;
                maxYLimit = PlayerPosition.y + 0.52f;

                break;
            case MovementState.sprinting:
                targetShakeSpeed = shakeSpeed * 1.5f;
                targetShakeIntensity = shakeIntensity * 1.5f;
                minYLimit = PlayerPosition.y + 1.14f;
                maxYLimit = PlayerPosition.y + 1.26f;
                break;
            default:
                // Default case (walking or any other state)
                targetShakeSpeed = shakeSpeed;
                targetShakeIntensity = shakeIntensity;
                minYLimit = PlayerPosition.y + 1.16f;
                maxYLimit = PlayerPosition.y + 1.24f;
                break;
        }

        // Smoothly interpolate the values
        shakeSpeedUsed = Mathf.Lerp(shakeSpeedUsed, targetShakeSpeed, Time.deltaTime * 5f);
        shakeIntensityUsed = Mathf.Lerp(shakeIntensityUsed, targetShakeIntensity, Time.deltaTime * 5f);

        if (PlayerRigidBody.velocity.magnitude > 0.1f)
        {

            MyInput();

            float elapsedTime = 0f;
            Vector3 initialScale = transform.localScale;
            Vector3 targetScale = new Vector3(transform.localScale.x, maxYLimit, transform.localScale.z);

            while(elapsedTime<1f)
            {
                float t = elapsedTime / 1.5f;
                transform.localScale = Vector3.Lerp(initialScale, targetScale, t);
                elapsedTime += Time.deltaTime;
            }

            // Below is part of the code responsible for screenshake itself without gradual change
            // Calculate the vertical offset using the sine function
            float yOffset = Mathf.Sin(timeElapsed * shakeSpeedUsed) * shakeIntensityUsed;

            // Apply the offset to the camera's position
            Vector3 newPosition = cameraPosition.position;
            newPosition.y += yOffset;

            // Clamp the camera's y-position within the desired limits
            newPosition.y = Mathf.Clamp(newPosition.y, minYLimit, maxYLimit);

            // Smoothly interpolate between current position and shaken position
            cameraPosition.position = Vector3.Lerp(cameraPosition.position, newPosition, Time.deltaTime * 10f);

            // Increment the time elapsed
            timeElapsed += Time.deltaTime;
        }
    }

    private void MyInput()
    {
        if(Input.GetKey(KeyCode.LeftControl))
        {
            cameraState = MovementState.crouching;
        } 
        else if(Input.GetKey(KeyCode.LeftShift))
        {
            cameraState = MovementState.sprinting;
        }
        else
        {
            cameraState = MovementState.walking;
        }
    }
}

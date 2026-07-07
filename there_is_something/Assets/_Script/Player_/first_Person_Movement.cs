using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class first_Person_Movement : MonoBehaviour
{


    [Header("Movement")]
    [SerializeField] private float speed = 5f;

    [Header("Mouse")]
    [SerializeField] private float mouseSensitivity = 3f;

    [Header("References")]
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private Transform playerCamera;

    [Header("Head Bob")]
    [SerializeField] private float bobSpeed = 8f;
    [SerializeField] private float bobAmount = 0.05f;
    [SerializeField] private float horizontalBobAmount = 0.02f;

    [Header("Idle Breathing")]
    [SerializeField] private float breathingSpeed = 1.2f;
    [SerializeField] private float breathingAmount = 0.005f;

    [Header("Camera Roll")]
    [SerializeField] private float rollAmount = 1.5f;

    private Rigidbody rb;

    private Vector3 movement;
    private Vector3 cameraStartPos;

    private float xRotation;
    private float bobTimer;
    private float currentBobAmount;
    private float currentRoll;

    [SerializeField] private bool verticalOnlyBob = true;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        cameraStartPos = playerCamera.localPosition;
    }

    private void Update()
    {
        RotatePlayer();
        HeadBob();
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    private void MovePlayer()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");

        movement = (transform.right * moveX + transform.forward * moveZ).normalized;

        rb.MovePosition(rb.position + movement * speed * Time.fixedDeltaTime);
    }

    private void RotatePlayer()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -45f, 30f);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, currentRoll);
    }

    private void HeadBob()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");

        bool isMoving = moveX != 0 || moveZ != 0;

        if (isMoving)
        {
            // Advance the walk cycle and ramp the bob strength up smoothly.
            bobTimer += Time.deltaTime * bobSpeed;
            currentBobAmount = Mathf.Lerp(currentBobAmount, 1f, Time.deltaTime * 6f);
        }
        else
        {
            // Ramp the bob strength down smoothly while standing still.
            currentBobAmount = Mathf.Lerp(currentBobAmount, 0f, Time.deltaTime * 6f);

            // Once the bob has fully faded, reset the cycle so the next step
            // always starts from the neutral pose instead of mid-stride.
            if (currentBobAmount < 0.01f)
            {
                currentBobAmount = 0f;
                bobTimer = 0f;
            }
        }

        // Vertical dip happens twice per stride (full frequency).
        float verticalBob = Mathf.Sin(bobTimer) * bobAmount;

        // Left/right sway happens once per stride (half frequency) so it
        // doesn't vibrate in lockstep with the vertical bob.
        float horizontalBob = verticalOnlyBob
            ? 0f
            : Mathf.Cos(bobTimer * 0.5f) * horizontalBobAmount;

        // Idle breathing fades in as the walk bob fades out.
        float breathing = Mathf.Sin(Time.time * breathingSpeed) * breathingAmount * (1f - currentBobAmount);

        Vector3 offset = cameraStartPos;
        offset.y += verticalBob * currentBobAmount + breathing;
        offset.x += horizontalBob * currentBobAmount;

        // Roll follows the sway (half frequency), not the vertical bob.
        float targetRoll = Mathf.Sin(bobTimer * 0.5f) * rollAmount * currentBobAmount;
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * 8f);

        // Apply the bob directly on top of the resting position. The sine wave
        // is already smooth, so lerping the whole position (as before) only
        // added lag and killed the effect — that was the "weird" part.
        playerCamera.localPosition = offset;
    }
}



using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class first_Person_Movement : MonoBehaviour
{

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 2.6f;
    [SerializeField] private float sprintSpeed = 4.6f;
    [SerializeField] private float speedAcceleration = 12f;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Mouse")]
    [SerializeField] private float mouseSensitivity = 3f;

    [Header("References")]
    [SerializeField] private Transform cameraHolder;

    private Rigidbody rb;

    private Vector3 movement;
    private float xRotation;
    private float yRotation;
    private float currentSpeed;

    /// <summary>Speed we are currently trying to move at. Ramped, so it never jumps.</summary>
    public float CurrentSpeed => currentSpeed;

    /// <summary>Top speed this controller can reach. Head_bob_system uses it to normalise.</summary>
    public float SprintSpeed => sprintSpeed;

    public bool IsSprinting { get; private set; }

    public Transform CameraHolder => cameraHolder;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        currentSpeed = walkSpeed;
        yRotation = transform.eulerAngles.y;

        if (cameraHolder == null)
        {
            Debug.LogError("first_Person_Movement on '" + name + "' has no cameraHolder assigned - " +
                           "look up/down will not work.", this);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        RotatePlayer();
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

        IsSprinting = Input.GetKey(sprintKey) && movement.sqrMagnitude > 0f;

        // Ramp rather than snap. Head bob amplitude follows real speed, so an instant
        // speed change would pop the bob.
        float targetSpeed = IsSprinting ? sprintSpeed : walkSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedAcceleration * Time.fixedDeltaTime);

        rb.MovePosition(rb.position + movement * currentSpeed * Time.fixedDeltaTime);
    }

    private void RotatePlayer()
    {
        if (cameraHolder == null) return;

        // GetAxisRaw, not GetAxis. Unity's legacy mouse axes run the delta through a smoothing
        // filter, which is literally added input lag on a look control.
        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        // Absolute, not transform.Rotate(). Rotate() is relative: it reads the transform and
        // adds to it. With Rigidbody interpolation on, the physics system also writes that
        // transform, so a relative rotate compounds onto interpolated values and jitters.
        // Keeping our own accumulator makes the yaw immune to whatever else touches the
        // transform.
        yRotation += mouseX;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}

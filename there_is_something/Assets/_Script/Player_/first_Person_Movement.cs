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
        ReadMoveInput();
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    /// <summary>
    /// Builds the world-space move direction. This runs in Update, immediately after the yaw
    /// is updated, and NOT in FixedUpdate - Unity runs every FixedUpdate before Update, so
    /// building the direction there would always use the previous frame's facing, and when the
    /// framerate drops below the physics rate several FixedUpdates run back to back with no
    /// Update between them, compounding the error into a visible "walks where I was looking".
    /// </summary>
    private void ReadMoveInput()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(moveX, 0f, moveZ);
        if (input.sqrMagnitude > 1f) input.Normalize();   // diagonals must not be faster

        // Rotate by the yaw accumulator, not by transform.right/transform.forward. The
        // transform is also written by Rigidbody interpolation between physics steps, so
        // reading it back is not a reliable source of truth. yRotation always is.
        movement = Quaternion.Euler(0f, yRotation, 0f) * input;

        IsSprinting = Input.GetKey(sprintKey) && input.sqrMagnitude > 0f;
    }

    private void MovePlayer()
    {
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

        // Keep the accumulator inside one turn. Left unbounded it grows every time you spin,
        // and once it is large enough a float can no longer resolve a small mouse delta -
        // look starts quantising after a long session. Wrapping costs nothing and is exact,
        // because a yaw of x and x + 360 are the same rotation.
        if (yRotation > 360f) yRotation -= 360f;
        else if (yRotation < -360f) yRotation += 360f;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}

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

    private Rigidbody rb;

    private Vector3 movement;
    private float xRotation;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

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

        rb.MovePosition(rb.position + movement * speed * Time.fixedDeltaTime);
    }

    private void RotatePlayer()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Rotate player left and right
        transform.Rotate(Vector3.up * mouseX);

        // Rotate camera up and down
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class first_Person_Movement : MonoBehaviour
{
    

    //[SerializeField]
    //private Animator anim;

    [SerializeField]
    private float speed;

    [SerializeField]
    private float mouseSensitivity;

    private Rigidbody rb;
    private Vector3 movement;

    private float xRotation;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // Prevents the character from tipping over
        Cursor.lockState = CursorLockMode.Locked; // Hides and locks the cursor
    }

    private void Update()
    {
        RotatePlayer();
        //MovePlayer();
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    private void MovePlayer()
    {
        float moveX = Input.GetAxisRaw("Horizontal"); // A/D or Left/Right arrow keys
        float moveZ = Input.GetAxisRaw("Vertical");   // W/S or Up/Down arrow keys

        // Calculate movement direction
        movement = (transform.right * moveX + transform.forward * moveZ).normalized;

        // Apply movement
        rb.MovePosition(rb.position + movement * speed * Time.fixedDeltaTime);


        //if (moveX > 0)
        //{
        //    anim.SetFloat("frunt_back", 1f);
        //}
        //if (moveX < 0)
        //{
        //    anim.SetFloat("frunt_back", -1f);
        //}
        //if (moveZ > 0)
        //{
        //    anim.SetFloat("left_Right", 1f);
        //}
        //if (moveZ < 0)
        //{
        //    anim.SetFloat("left_Right", -1f);
        //}
        //if (moveX == 0 && moveZ == 0)
        //{
        //    anim.SetFloat("frunt_back", 0);
        //    anim.SetFloat("left_Right", 0);
        //}

    }

    private void RotatePlayer()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Rotate character around the Y axis
        transform.Rotate(Vector3.up * mouseX);

        // Rotate the camera vertically
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -45f, 30f); // Clamps the rotation to prevent over-rotation

        Camera.main.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}




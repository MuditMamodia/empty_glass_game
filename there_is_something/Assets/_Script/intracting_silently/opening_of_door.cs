using UnityEngine;

public class opening_of_door : MonoBehaviour, horror_silent_scripting
{
    [Header("Movement")]
    public Transform drawerObject; // the moving part
    public float openSpeed;
    public float maxOpenDistance;
    //public float opened_position_checked;

    private float currentY;
    private float startY;
    private float targetY;

    private Quaternion startPos;
    public bool isInteracting = false;

    [SerializeField] private AudioSource drawerAudio;

    public bool isopened = false;
    public bool isclosed = true;

    public first_Person_Movement fpm;

    public float interactDistance = 2f;

   

    public float GetInteractDistance()
    {
        return interactDistance;
    }

    public bool interacting()
    {
        return isInteracting;
    }

    private void Start()
    {
        isclosed = true;
        isopened = false;
        startY = drawerObject.localEulerAngles.y;
        currentY = startY;
        targetY = startY + maxOpenDistance;
    }

    public void Interactable()
    {
        isInteracting = true;

        // Lock cursor for dragging
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log("Started interacting with drawer");
    }

    private void Update()
    {
        if (isclosed)
        {
            
            opening_it();
        }
        else if (isopened)
        {
            closing_it();
        }

        if (isInteracting)
        {
            fpm.enabled = false;
        }
        else
        {
            fpm.enabled = true;
        }
    }

    public void opening_it()
    {
        if (!isInteracting) return;

       
        float mouseY = Input.GetAxis("Mouse Y");

        currentY += mouseY * openSpeed * Time.deltaTime;

        currentY = Mathf.Clamp(currentY, startY, targetY);

        drawerObject.localRotation = Quaternion.Euler(0f, currentY, 0f);

        HandleSound(mouseY);

        if (Mathf.Abs(currentY - targetY) < 0.5f)
        {
            isopened = true;
            isclosed = false;
            StopInteraction();
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            StopInteraction();
        }
    }
    public void closing_it()
    {
        if (!isInteracting) return;

        float mouseY = Input.GetAxis("Mouse Y");

        currentY += mouseY * openSpeed * Time.deltaTime;

        currentY = Mathf.Clamp(currentY, startY, targetY);

        drawerObject.localRotation = Quaternion.Euler(0f, currentY, 0f);

        HandleSound(mouseY);

        if (Mathf.Abs(currentY - startY) < 0.5f)
        {
            isopened = false;
            isclosed = true;
            StopInteraction();
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            StopInteraction();
        }
    }

    private void HandleSound(float mouseY)
    {
        //float speed = Mathf.Abs(mouseY);

        //float volume = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(speed * 2f));
        //drawerAudio.volume = volume;

        //if (speed > 0.01f)
        //{
        //    if (!drawerAudio.isPlaying)
        //        drawerAudio.Play();
        //}
        //else
        //{
        //    drawerAudio.Pause();
        //}
    }

    private void StopInteraction()
    {
        isInteracting = false;

        //drawerAudio.Stop(); // 🔥 stop sound

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log("Stopped interacting");
    }

}

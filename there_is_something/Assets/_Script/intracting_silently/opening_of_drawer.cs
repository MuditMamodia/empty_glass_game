using UnityEngine;

public class opening_of_drawer : MonoBehaviour, horror_silent_scripting
{
    [Header("Movement")]
    public Transform drawerObject; // the moving part
    public float openSpeed ;
    public float maxOpenDistance ;
    public float opened_position_checked;

    private Vector3 startPos;
    public bool isInteracting = false;

    [SerializeField] private AudioSource drawerAudio;

    public bool isopened = false;
    public bool isclosed = true;
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
        startPos = drawerObject.localPosition;
        opened_position_checked = startPos.z + maxOpenDistance;
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
    }

    public void opening_it()
    {
        if (!isInteracting) return;

        float mouseY = Input.GetAxis("Mouse Y");

        // Move drawer
        Vector3 move = drawerObject.localPosition;
        move.z += mouseY * openSpeed * Time.deltaTime;

        float clampedZ = Mathf.Clamp(move.z, startPos.z, startPos.z + maxOpenDistance);
        drawerObject.localPosition = new Vector3(startPos.x, startPos.y, clampedZ);

        // 🔥 SOUND CONTROL
        float speed = Mathf.Abs(mouseY);

        // Map speed to volume (0 → 1)
        float volume = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(speed * 2f)); // multiply to boost sensitivity

        drawerAudio.volume = volume;

        // Play sound only when moving
        if (speed > 0.01f)
        {
            if (!drawerAudio.isPlaying)
                drawerAudio.Play();
        }
        else
        {
            //drawerAudio.Stop();
        }

        if (Mathf.Abs(drawerObject.localPosition.z - opened_position_checked) < 0.01f)
        {
            StopInteraction();
            isopened = true;
            isclosed = false;
        }

        // Exit interaction
        if (Input.GetKeyDown(KeyCode.E))
        {
            StopInteraction();
        }
    }
    public void closing_it()
    {
        if (!isInteracting) return;

        float mouseY = Input.GetAxis("Mouse Y");

        // Move drawer
        Vector3 move = drawerObject.localPosition;
        move.z += mouseY * openSpeed * Time.deltaTime;

        float clampedZ = Mathf.Clamp(move.z, startPos.z, startPos.z + maxOpenDistance);
        drawerObject.localPosition = new Vector3(startPos.x, startPos.y, clampedZ);

        // 🔥 SOUND CONTROL
        float speed = Mathf.Abs(mouseY);

        // Map speed to volume (0 → 1)
        float volume = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(speed * 2f)); // multiply to boost sensitivity

        drawerAudio.volume = volume;

        // Play sound only when moving
        if (speed > 0.01f)
        {
            if (!drawerAudio.isPlaying)
                drawerAudio.Play();
        }
        else
        {
            //drawerAudio.Stop();
        }

        if (Mathf.Abs(drawerObject.localPosition.z - startPos.z) < 0.01f)
        {
            StopInteraction();
            isopened = false;
            isclosed = true;
        }

        // Exit interaction
        if (Input.GetKeyDown(KeyCode.E))
        {
            StopInteraction();
        }
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

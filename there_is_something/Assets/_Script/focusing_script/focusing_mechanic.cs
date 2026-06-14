using UnityEngine;
using UnityEngine.Rendering.Universal;

public class focusing_mechanic : MonoBehaviour,horror_silent_scripting
{
    public first_Person_Movement fpm;
    public Camera cam;
    public Vector3 offset;

    private bool focusing;

    public float interactDistance = 2f;

    public float GetInteractDistance()
    {
        return interactDistance;
    }

    public bool interacting()
    {
        return focusing;
    }

    private void Start()
    {
        cam = Camera.main;
    }

    public void Interactable()
    {
        focusing = !focusing;

        if (focusing)
        {
            fpm.enabled = false;

            cam.transform.position = transform.position + offset;
            cam.transform.LookAt(transform);
        }
        else
        {
            fpm.enabled = true;
        }
    }
}
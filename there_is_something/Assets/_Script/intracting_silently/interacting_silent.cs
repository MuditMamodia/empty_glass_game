using UnityEngine;
using System.Collections;

public class interacting_silent : MonoBehaviour
{
    public GameObject center_of_radius;
    public float maxdistance;
    public LayerMask intractiable_layer;
    public Transform inspection_postion;

    ispect_mechanic im;
    private horror_silent_scripting currentInteractable;

    //bool isintracting;

    private void Update()
    {
        RaycastHit hit;

        Vector3 origin = center_of_radius.transform.position;
        Vector3 direction = center_of_radius.transform.forward;

        currentInteractable = null;

        if (Physics.Raycast(origin, direction, out hit, maxdistance, intractiable_layer))
        {
            horror_silent_scripting interactable =
                hit.collider.GetComponent<horror_silent_scripting>();

            if (interactable != null)
            {
                float distance = hit.distance;
                Debug.Log("intractanble area");
                ispect_mechanic im =
                    hit.collider.GetComponent<ispect_mechanic>();

                if (im != null)
                {
                    im.postiontogo = inspection_postion;
                }

                if (distance <= interactable.GetInteractDistance() )
                {
                    currentInteractable = interactable;

                    Debug.Log("Valid Interaction");

                    // PRESS E
                    if (Input.GetKeyDown(KeyCode.E))
                    {
                        interactable.Interactable();
                    }

                    // HOLD E
                    if (Input.GetKey(KeyCode.E))
                    {
                        interactable.interacting();
                    }
                }
            }
        }
    }
}

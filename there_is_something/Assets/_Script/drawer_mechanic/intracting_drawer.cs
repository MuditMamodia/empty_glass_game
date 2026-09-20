using UnityEngine;

public class intracting_drawer : MonoBehaviour
{
    [Header("Raycast Settings")]
    public float rayDistance = 3f;

    [Header("Interaction")]
    public KeyCode interactionKey = KeyCode.F;

    private void Update()
    {
        // Only check when F is pressed
        if (Input.GetKeyDown(interactionKey))
        {
            ShootRaycast();
        }
    }

    private void ShootRaycast()
    {
        Ray ray = new Ray(
            transform.position,
            transform.forward
        );

        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, rayDistance))
        {
            drawer_mechanic drawer =
                hit.collider.GetComponent<drawer_mechanic>();

            if (drawer == null)
            {
                drawer =
                    hit.collider.GetComponentInParent<drawer_mechanic>();
            }

            if (drawer != null)
            {
                // Toggle open / close
                drawer.is_opened = !drawer.is_opened;
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;

        Gizmos.DrawRay(
            transform.position,
            transform.forward * rayDistance
        );
    }
}

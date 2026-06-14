using UnityEngine;


/// <summary>
/// this is the functionality script which will be added in the player so that it could pick up itmes
/// </summary>
public class picking_up_of_items : MonoBehaviour
{


    public inventory_brain ib;
    
    private void Update()
    {
        Debug.DrawRay(transform.position, transform.forward* 10f, Color.red);
        if (Input.GetKeyDown(KeyCode.F))
        {
            RaycastHit hit;

            if (Physics.Raycast(transform.position, transform.forward, out hit, 10f))
            {
                Debug.Log("Hit: " + hit.collider.name);
                items_canbecollected_data item = hit.collider.GetComponent<items_canbecollected_data>();

                if (item != null)
                {
                    bool picked = ib.PickupItem(item.itemData);

                    if (picked)
                    {
                        Debug.Log("item picked successfully");
                        Destroy(hit.collider.gameObject); // ✅ destroy only if picked
                    }
                    else
                    {
                        Debug.Log("item NOT picked");
                    }
                }
            }
        }
    }
}

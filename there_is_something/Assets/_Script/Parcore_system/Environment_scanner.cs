using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public struct obstical_hit_data
{
    public bool obstical_detector;
    public bool height_hit_gound;
    public RaycastHit hitforword;
    public RaycastHit heighthit;
    
}


public class Environment_scanner : MonoBehaviour
{
    [SerializeField] Vector3 forword_ray_offset;
    [SerializeField] float raylenth;
    [SerializeField] float hight_ray_length;
    [SerializeField] bool height_object;
    public bool normaljump;
    [SerializeField] float normaljumpheight;
    [SerializeField] LayerMask obstical_Layer;
    obstical_hit_data hitdata;
    [Header("Wall jump")]
    [SerializeField] float min_height_of_object_for_walljump;
    [SerializeField] float max_heiht_of_object_for_walljump;
    public bool canwalljump;

    [SerializeField] float object_height;

    private void FixedUpdate()
    {
        obsitcalchecker();
        height_checker();
    }

    public void height_checker()
    {
        if (hitdata.obstical_detector)
        {
             object_height = hitdata.hitforword.collider.bounds.size.y;
            Debug.Log(object_height);
            if (object_height >= min_height_of_object_for_walljump && object_height <= max_heiht_of_object_for_walljump)
            {
                canwalljump = true;
            }
            if (object_height <= normaljumpheight)
            {
                normaljump = true;
            }
            
        }
        else
        {
            canwalljump = false;
            normaljump = false;
        }
    }


    public obstical_hit_data obsitcalchecker()
    {
        

        var rayorigin = transform.position + forword_ray_offset;

        
        hitdata.obstical_detector = Physics.Raycast(rayorigin, transform.forward, out hitdata.hitforword, raylenth, obstical_Layer);

        // Visualize the ray in the Scene view
        if (hitdata.obstical_detector)
        {
            Debug.DrawRay(rayorigin, transform.forward * raylenth, Color.red); // Red for hit
        }
        else
        {
            Debug.DrawRay(rayorigin, transform.forward * raylenth, Color.green); // Green for no hit
        }


        if(hitdata.obstical_detector)
        {
            var hightorigin = hitdata.hitforword.point + Vector3.up * hight_ray_length;
            hitdata.height_hit_gound = Physics.Raycast(hightorigin, Vector3.down, out hitdata.heighthit, hight_ray_length, obstical_Layer);
            Debug.DrawRay(hightorigin, Vector3.down * hight_ray_length, Color.red); // Red for hit
        }

        return hitdata;

    }
}

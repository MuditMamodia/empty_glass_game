using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class parcore_cuntroller : MonoBehaviour
{
    public Environment_scanner environementscanner;
    public Animator anim;
    public Rigidbody rb;

    public bool noramljummmmm;


    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        anim= GetComponent<Animator>();
        environementscanner = GetComponent<Environment_scanner>();
    }
    private void Update()
    {
        if (environementscanner.normaljump)
        {
            noramljummmmm = true;
            if (Input.GetKeyDown(KeyCode.Space))
            {
                StartCoroutine(step_up_animation("normal_jump"));
                var hitdata = environementscanner.obsitcalchecker();
                if (hitdata.obstical_detector)
                {
                   
                }
            }
        }
        else
        {
            noramljummmmm = false;
        }
        

        if(environementscanner.canwalljump)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                StartCoroutine(doing_walljumpanim("wall_climb"));
            }
        }
    }

    IEnumerator doing_walljumpanim(string animaionname)
    {
        anim.SetBool("animaionname", true);
        anim.applyRootMotion = true;
        rb.isKinematic = true;
        yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length);
        anim.SetBool("animaionname", false);
        anim.applyRootMotion = false;
        rb.isKinematic = false;
    }
    


    IEnumerator step_up_animation(string animationname)
    {
        anim.SetBool("animationname", true);
        anim.applyRootMotion = true;
        rb.isKinematic = true;
        yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length);
        anim.SetBool("animationname", false);
        anim.applyRootMotion = false;
        rb.isKinematic = false;
    }
}

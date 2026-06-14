using UnityEngine;

public class scripts_enable_and_disable_manager : MonoBehaviour
{
    public static scripts_enable_and_disable_manager sedm;
    public first_Person_Movement fpm;

    private void Awake()
    {
        sedm = this;
    }


    public void enabling_the_script()
    {
        fpm.enabled = true;
    }

    public void disabling_the_script()
    {
        fpm.enabled = false;
    }
}

using UnityEngine;

public class scripts_disable_container : MonoBehaviour
{
    public static scripts_disable_container sdc;


    public Head_bob_system  hbs;
    public first_Person_Movement fpm;


    private void Awake()
    {
        sdc = this;
    }

    public void player_all_script_changer(bool change)
    {
        hbs.enabled = change;
        fpm.enabled = change;
    }
   

}

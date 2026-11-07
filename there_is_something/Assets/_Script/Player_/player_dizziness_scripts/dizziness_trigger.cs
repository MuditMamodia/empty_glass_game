using UnityEngine;

public class dizziness_trigger : MonoBehaviour
{
    

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            dizziness_effect.dizzi_effecect._player_gettriggered = true;
        }
    }
}

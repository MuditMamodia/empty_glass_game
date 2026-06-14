using UnityEngine;

public class triggring_test : MonoBehaviour
{
    [SerializeField]private eyes_blinking eyeblinking;

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            eyeblinking.slow_shutdown();
        }
    }
}

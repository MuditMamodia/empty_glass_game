using UnityEngine;

public class dizziness_trigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("Player")) return;

        // Null-checked because the static starts out empty: if there is no dizziness_effect in
        // the scene, or it is switched off, nothing ever filled it in.
        if (dizziness_effect.dizzi_effecect == null)
        {
            Debug.LogWarning("dizziness_trigger on '" + name + "': there is no active " +
                             "dizziness_effect in the scene, so nothing can be triggered.", this);
            return;
        }

        // The PROPERTY, not the _player_gettriggered field. The field is just a box; the property
        // is the box with the code behind it, and only the property starts the animation.
        dizziness_effect.dizzi_effecect.player_gettingtrigger = true;
    }
}

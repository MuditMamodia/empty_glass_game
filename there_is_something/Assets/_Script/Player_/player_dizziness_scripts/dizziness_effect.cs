using UnityEngine;
using System.Collections;

public class dizziness_effect : MonoBehaviour
{
    public static dizziness_effect dizzi_effecect;

    // Private, so the only way in from another script is through the property below. While this
    // was public it was possible to set the field directly and skip the set block entirely, which
    // looks like it works and silently does nothing.
    [SerializeField] private bool _player_gettriggered;

    private void Awake()
    {
        dizzi_effecect = this;
    }

    public bool player_gettingtrigger
    {
        get { return _player_gettriggered; }
        set
        {
            // == , not = . One equals sign is an assignment, and it would put the value in and
            // then return straight away, so nothing below ever ran.
            if (_player_gettriggered == value) return;

            _player_gettriggered = value;

            if (_player_gettriggered)
            {
                StartCoroutine(dizzyness_calling_anim());
            }
        }
    }

    IEnumerator dizzyness_calling_anim()
    {
        // Both start together. The faint and the collapse are both 10 seconds, so the eyelids
        // finish closing at the moment the head reaches the floor.
        eye_effect.Instance.PlayFaint();
        if (player_collapse.Instance != null) player_collapse.Instance.PlayCollapse();

        // 10s of going down, then 5s lying on the floor in the dark.
        yield return new WaitForSeconds(15);

        // Both 10 seconds again: the eye opens as he pushes himself up.
        eye_effect.Instance.PlayWakeUp();
        if (player_collapse.Instance != null) player_collapse.Instance.PlayGetUp();

        yield return new WaitForSeconds(12);

        // Safety net. Both animations have already finished clear by now; this only matters if
        // one of them was interrupted.
        eye_effect.Instance.SetOpenInstant(1f);
        if (player_collapse.Instance != null) player_collapse.Instance.ResetInstant();

        // Leave this commented out and the whole thing happens once. Uncomment it to let the
        // player be knocked out again by the same trigger later.
        // _player_gettriggered = false;
    }
}

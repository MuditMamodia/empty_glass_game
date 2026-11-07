using UnityEngine;
using System.Collections;

public class dizziness_effect : MonoBehaviour
{
    public static dizziness_effect dizzi_effecect;
    public bool _player_gettriggered;

    private void Awake()
    {
        dizzi_effecect = this;
    }

    public bool player_gettingtrigger
    {
        get { return _player_gettriggered; }
        set 
        {
            if (_player_gettriggered = value) return;
            _player_gettriggered = value;
            if (_player_gettriggered)
            {
                eye_effect.Instance.PlayFaint();
                StartCoroutine(dizzyness_calling_anim());
            }
        }
    }

    IEnumerator dizzyness_calling_anim()
    {
        yield return new WaitForSeconds(15);
        eye_effect.Instance.PlayWakeUp();

        yield return new WaitForSeconds(12);
        eye_effect.Instance.SetOpenInstant(1f);
    }
}

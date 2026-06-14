using UnityEngine;
using System.Collections;

public class eyes_blinking : MonoBehaviour
{
    public Transform eyelesh_up;
    public Transform eyelesh_down;

    public Vector3 up_eye_rotation_euler;
    public Vector3 down_eye_rotation_euler;

    Quaternion up_eyelish_origenal_rotation;
    Quaternion down_eyelish_origenal_rotation;

    public float roation_speed;

    private void Start()
    {
        up_eyelish_origenal_rotation = eyelesh_up.rotation;
        down_eyelish_origenal_rotation = eyelesh_down.rotation;
    }

    public void slow_shutdown()
    {
        StartCoroutine(shutdown_rotation_up_eyelish());
        StartCoroutine(shutdown_rotation_down_eyelish());
    }

    IEnumerator shutdown_rotation_up_eyelish()
    {
        float currentX = eyelesh_up.localEulerAngles.x;
        float targetX = up_eye_rotation_euler.x;

        while (Mathf.Abs(Mathf.DeltaAngle(currentX, targetX)) > 0.1f)
        {
            currentX = Mathf.MoveTowardsAngle(
                currentX,
                targetX,
                roation_speed * 100f * Time.deltaTime
            );

            // 🔥 DO NOT read localEulerAngles
            eyelesh_up.localRotation = Quaternion.Euler(currentX, 0f, 0f);

            yield return null;
        }

        eyelesh_up.localRotation = Quaternion.Euler(targetX, 0f, 0f);
    }
    IEnumerator shutdown_rotation_down_eyelish()
    {

        float currentX = eyelesh_down.localEulerAngles.x;
        float targetX = down_eye_rotation_euler.x;

        while (Mathf.Abs(Mathf.DeltaAngle(currentX, targetX)) > 0.1f)
        {
            currentX = Mathf.MoveTowardsAngle(
                currentX,
                targetX,
                roation_speed * 100f * Time.deltaTime
            );

            // 🔥 DO NOT read localEulerAngles
            eyelesh_down.localRotation = Quaternion.Euler(currentX, 0f, 0f);

            yield return null;
        }

        eyelesh_down.localRotation = Quaternion.Euler(targetX, 0f, 0f);
    }

}

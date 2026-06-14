using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class ispect_mechanic : MonoBehaviour, horror_silent_scripting
{
    public float interactDistance = 2f;

    public Vector3 origenal_postion;
    public Transform postiontogo;

    public bool isinteracting;

    private float currentY;
    private float startY;
    private float targetY;

    public float openSpeed;

   

    public float GetInteractDistance()
    {
        return interactDistance;
    }

    public bool interacting()
    {
        return isinteracting;
    }

    private void Awake()
    {
        origenal_postion = transform.position;

        startY = transform.localEulerAngles.y;
        targetY = startY + 180f; // 🔥 rotation limit
        currentY = startY;
    }

    public void Interactable()
    {
        isinteracting = true;

    }

    private void Update()
    {

        if (isinteracting)
        {
            scripts_enable_and_disable_manager.sedm.disabling_the_script();
            transform.position = postiontogo.position;

            float mouseY = Input.GetAxis("Mouse X");

            currentY += mouseY * openSpeed * Time.deltaTime;

            currentY = Mathf.Clamp(currentY, startY, targetY);

            transform.localRotation = Quaternion.Euler(0f, currentY, 0f);
        }
        //else if (!isinteracting)
        //{

        //    scripts_enable_and_disable_manager.sedm.enabling_the_script();

        //}
        
        
    }
}
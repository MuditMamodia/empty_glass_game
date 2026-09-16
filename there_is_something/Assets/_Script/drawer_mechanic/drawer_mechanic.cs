using UnityEngine;

public class drawer_mechanic : MonoBehaviour
{
    [Header("Open / Close")]
    public bool is_opened = false;

    [Header("Position")]
    public bool checkPositionX = false;
    public bool checkPositionY = false;
    public bool checkPositionZ = false;

    [Tooltip("How much the object moves on each axis when opened.")]
    public float positionXAmount = 0f;
    public float positionYAmount = 0f;
    public float positionZAmount = 0f;

    [Header("Rotation")]
    public bool checkRotationX = false;
    public bool checkRotationY = false;
    public bool checkRotationZ = false;

    [Tooltip("How many degrees the object rotates on each axis when opened.")]
    public float rotationXAmount = 0f;
    public float rotationYAmount = 0f;
    public float rotationZAmount = 0f;

    [Header("Smooth Settings")]
    [Min(0.01f)]
    public float positionSmoothSpeed = 5f;

    [Min(0.01f)]
    public float rotationSmoothSpeed = 5f;

    private Vector3 closedPosition;
    private Quaternion closedRotation;

    private Vector3 openPosition;
    private Quaternion openRotation;

    private void Awake()
    {
        // Store the original state as the CLOSED state.
        closedPosition = transform.localPosition;
        closedRotation = transform.localRotation;

        CalculateOpenState();
    }

    private void Update()
    {
        if (is_opened)
        {
            MoveToOpen();
        }
        else
        {
            MoveToClosed();
        }
    }

    private void CalculateOpenState()
    {
        // -------------------------
        // POSITION
        // -------------------------

        openPosition = closedPosition;

        if (checkPositionX)
        {
            openPosition.x = closedPosition.x + positionXAmount;
        }

        if (checkPositionY)
        {
            openPosition.y = closedPosition.y + positionYAmount;
        }

        if (checkPositionZ)
        {
            openPosition.z = closedPosition.z + positionZAmount;
        }


        // -------------------------
        // ROTATION
        // -------------------------

        Vector3 closedEuler = closedRotation.eulerAngles;

        Vector3 openEuler = closedEuler;

        if (checkRotationX)
        {
            openEuler.x += rotationXAmount;
        }

        if (checkRotationY)
        {
            openEuler.y += rotationYAmount;
        }

        if (checkRotationZ)
        {
            openEuler.z += rotationZAmount;
        }

        openRotation = Quaternion.Euler(openEuler);
    }

    private void MoveToOpen()
    {
        // Smooth position
        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition,
            openPosition,
            positionSmoothSpeed * Time.deltaTime
        );

        // Smooth rotation
        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            openRotation,
            rotationSmoothSpeed * 100f * Time.deltaTime
        );
    }

    private void MoveToClosed()
    {
        // Smooth position
        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition,
            closedPosition,
            positionSmoothSpeed * Time.deltaTime
        );

        // Smooth rotation
        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            closedRotation,
            rotationSmoothSpeed * 100f * Time.deltaTime
        );
    }
}

using UnityEngine;
using UnityEngine.UI;

public class door_opening_and_closing : MonoBehaviour, horror_silent_scripting
{
    [Header("Door Settings")]
    public float Direction = 1f;
    public float maxrotation = 90f;

    [Header("Speed Settings")]
    public float slowOpenSpeed = 35f;
    public float fastOpenSpeed = 300f;

    [Header("Hold Detection")]
    [Tooltip("How long (seconds) E must stay down before it counts as a HOLD instead of a TAP.")]
    public float holdThreshold = 0.15f;

    [Header("Interaction")]
    public float interactDistance = 3f;

    [Header("Hold UI (assign in inspector)")]
    [Tooltip("Parent GameObject that contains the outline image + fill bar. Will be shown while holding E and hidden on release.")]
    public GameObject holdUI;
    [Tooltip("Inner fill image. Image Type must be set to 'Filled' (Horizontal / Vertical / Radial) in the Inspector.")]
    public Image holdFillBar;
    [Tooltip("How long (seconds) it takes for the fill bar to reach full while E is held.")]
    public float fillFullTime = 1.5f;
    [Tooltip("How snappy the UI pop-in / pop-out scale animation feels.")]
    public float uiPopSpeed = 14f;

    private Transform door;
    private Quaternion closedRotation;
    private Quaternion openRotation;

    private bool isOpen = false;
    private float currentSpeed;

    private bool isHeldThisFrame;
    private bool wasHeldLastFrame;
    private float pressStartTime;
    private bool isInHoldMode;

    private RectTransform uiRect;
    private Vector3 uiBaseScale = Vector3.one;
    private Vector3 uiTargetScale = Vector3.zero;

    public locked_door lockedDoor;

    private void Awake()
    {
        door = transform;
        closedRotation = door.localRotation;

        openRotation = Quaternion.Euler(
            transform.localEulerAngles.x,
            transform.localEulerAngles.y + (maxrotation * Direction),
            transform.localEulerAngles.z
        );

        if (holdUI != null)
        {
            uiRect = holdUI.GetComponent<RectTransform>();
            if (uiRect != null)
            {
                uiBaseScale = uiRect.localScale;
                uiRect.localScale = Vector3.zero;
            }
            holdUI.SetActive(false);
        }

        if (holdFillBar != null)
            holdFillBar.fillAmount = 0f;
    }

    private void Update()
    {
        // Release edge: held last frame but not this one -> finger came off
        if (wasHeldLastFrame && !isHeldThisFrame)
            OnReleased();

        wasHeldLastFrame = isHeldThisFrame;
        isHeldThisFrame = false;

        // Door rotation
        Quaternion targetRotation = isOpen ? openRotation : closedRotation;
        door.localRotation = Quaternion.RotateTowards(
            door.localRotation,
            targetRotation,
            currentSpeed * Time.deltaTime
        );

        // UI pop in/out
        if (uiRect != null)
        {
            uiRect.localScale = Vector3.Lerp(
                uiRect.localScale,
                uiTargetScale,
                Time.deltaTime * uiPopSpeed
            );

            if (uiTargetScale == Vector3.zero
                && holdUI.activeSelf
                && uiRect.localScale.sqrMagnitude < 0.0005f)
            {
                uiRect.localScale = Vector3.zero;
                holdUI.SetActive(false);
            }
        }
    }

    // KeyDown — single tap starts a FAST open/close immediately.
    public void Interactable()
    {
        if (lockedDoor != null)
        {
            if (!lockedDoor.TryUnlock())
            {
                Debug.Log("Door is locked");
                return;
            }
        }

        pressStartTime = Time.time;
        isInHoldMode = false;

        currentSpeed = fastOpenSpeed;
        isOpen = !isOpen;
    }

    // Called every frame E is held while the raycast hits this door.
    public bool interacting()
    {
        isHeldThisFrame = true;

        float heldDuration = Time.time - pressStartTime;

        if (heldDuration >= holdThreshold)
        {
            if (!isInHoldMode)
            {
                isInHoldMode = true;
                currentSpeed = slowOpenSpeed;

                if (holdUI != null)
                {
                    holdUI.SetActive(true);
                    uiTargetScale = uiBaseScale;
                }
            }

            if (holdFillBar != null)
            {
                float t = Mathf.Clamp01((heldDuration - holdThreshold) / Mathf.Max(0.0001f, fillFullTime));
                holdFillBar.fillAmount = t;
            }
        }

        return true;
    }

    // Player let go of E while we were in hold mode -> snap to fast for the rest of the motion.
    private void OnReleased()
    {
        if (isInHoldMode)
            currentSpeed = fastOpenSpeed;

        isInHoldMode = false;
        uiTargetScale = Vector3.zero;

        if (holdFillBar != null)
            holdFillBar.fillAmount = 0f;
    }

    public float GetInteractDistance()
    {
        return interactDistance;
    }
}

using UnityEngine;

/// <summary>
/// Layered procedural first-person camera motion.
///
/// Attach this to the CAMERA transform, not to the pivot that mouse-look writes to.
/// In this project that means Main Camera (a child of camera_holder), because
/// first_Person_Movement overwrites camera_holder.localRotation every frame and would
/// destroy any rotation written there.
///
/// Layers, all independent and additive:
///   - gait bob      figure-8 path driven by measured speed, vertical at 2x lateral
///   - roll / nod    small rotation coupled to the gait
///   - spring        critically damped dip for landings and per-footstep micro-impulses
///   - breathing     very low idle motion, cross-faded against the gait
///   - look sway     slight counter-offset to mouse movement
/// </summary>
public class Head_bob_system : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player root (the object with the Rigidbody). Auto-resolved from parents if left empty.")]
    [SerializeField] private Transform playerRoot;
    [Tooltip("Optional. While this movement script is disabled, something else owns the camera " +
             "(focusing_mechanic, ispect_mechanic) and the bob suspends itself instead of fighting it.")]
    [SerializeField] private first_Person_Movement movementScript;
    [Tooltip("Cancels the parent's pitch out of the positional bob, so looking straight down does " +
             "not turn vertical bob into forward/back dolly.")]
    [SerializeField] private bool pitchIndependentTranslation = true;

    [Header("Gait")]
    [Tooltip("Strides per second at full speed. One stride = two footfalls.")]
    [SerializeField, Range(0.2f, 2.5f)] private float strideFrequency = 0.9f;
    [Tooltip("Peak vertical bob in metres.")]
    [SerializeField, Range(0f, 0.12f)] private float verticalAmount = 0.035f;
    [Tooltip("Peak side-to-side bob in metres. Roughly 0.6x vertical reads as walking.")]
    [SerializeField, Range(0f, 0.12f)] private float lateralAmount = 0.022f;
    [Tooltip("Speed treated as 'full bob'. Match your sprint speed.")]
    [SerializeField] private float referenceSpeed = 4.6f;
    [SerializeField] private float speedSmoothing = 8f;
    [Tooltip("Frame speeds above this are treated as a teleport and ignored.")]
    [SerializeField] private float teleportRejectSpeed = 20f;
    [SerializeField] private float airborneFadeTime = 0.12f;

    [Header("Rotation (degrees)")]
    [SerializeField, Range(0f, 3f)] private float rollAmount = 0.4f;
    [SerializeField, Range(0f, 3f)] private float nodAmount = 0.25f;

    [Header("Spring & Impact")]
    [SerializeField] private float stiffness = 180f;
    [SerializeField] private float damping = 22f;
    [Tooltip("Dip strength for a landing at Land Reference Fall Speed.")]
    [SerializeField] private float landImpulse = 0.9f;
    [SerializeField] private float landReferenceFallSpeed = 8f;
    [SerializeField, Range(0f, 0.3f)] private float footstepImpulse = 0.05f;
    [SerializeField, Range(0f, 1f)] private float footstepMinSpeedFactor = 0.15f;
    [SerializeField] private float landingDebounce = 0.08f;

    [Header("Breathing")]
    [SerializeField] private float breathFrequency = 0.25f;
    [SerializeField, Range(0f, 0.03f)] private float breathAmount = 0.006f;

    [Header("Look Sway")]
    [Tooltip("Off by default. This drags the camera opposite to your mouse, which adds mass but " +
             "also reads as look latency. Raise slowly (try 0.002) only if you want that weight.")]
    [SerializeField, Range(0f, 0.02f)] private float swayAmount;
    [SerializeField] private float swaySmoothing = 9f;
    [SerializeField, Range(0f, 0.06f)] private float swayMaxOffset = 0.015f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("How far below the capsule bottom still counts as grounded.")]
    [SerializeField] private float groundCheckDistance = 0.25f;
    [Tooltip("Probe radius as a fraction of the capsule radius. Below 1 so it cannot catch on walls.")]
    [SerializeField, Range(0.5f, 0.99f)] private float groundProbeRadiusScale = 0.9f;
    [Tooltip("Used only when the player root has no CapsuleCollider.")]
    [SerializeField] private float fallbackProbeRadius = 0.4f;
    [Tooltip("Used only when the player root has no CapsuleCollider. Feet height in root local space.")]
    [SerializeField] private float fallbackFeetLocalY;

    /// <summary>Fired on each footfall. Argument is speed factor 0..1, for volume / pitch.</summary>
    public event System.Action<float> OnFootstep;

    /// <summary>0 when standing still or airborne, 1 at reference speed.</summary>
    public float SpeedFactor => speedFactor;

    public bool IsGrounded => isGrounded;

    private const float TwoPi = Mathf.PI * 2f;
    private const float SpringSubstep = 0.005f;

    private readonly RaycastHit[] groundHits = new RaycastHit[16];

    private Vector3 basePos;
    private Quaternion baseRot;

    private Vector3 lastRootPos;
    private float measuredSpeed;
    private float verticalSpeed;
    private float speedFactor;
    private float groundedBlend = 1f;

    private float bobPhase;
    private float breathPhase;

    private float springPos;
    private float springVel;

    private Vector3 swayOffset;

    private bool isGrounded = true;
    private bool suspended;
    private float landingCooldown;

    private CapsuleCollider playerCapsule;
    private Vector3 probeLocalOrigin;   // bottom sphere centre, in playerRoot local space
    private float probeRadius;
    private float probeSlack;           // gap between the probe sphere and the capsule bottom

    private void Awake()
    {
        basePos = transform.localPosition;
        baseRot = transform.localRotation;

        if (playerRoot == null)
        {
            Rigidbody parentBody = GetComponentInParent<Rigidbody>();
            if (parentBody != null) playerRoot = parentBody.transform;
        }

        if (playerRoot == null)
        {
            Debug.LogError("Head_bob_system on '" + name + "': no Player Root assigned and no " +
                           "Rigidbody found in parents. Disabling.", this);
            enabled = false;
            return;
        }

        if (movementScript == null) movementScript = playerRoot.GetComponent<first_Person_Movement>();

        lastRootPos = playerRoot.position;
        RefreshGroundProbe();
    }

    /// <summary>
    /// Derives the ground probe from the player's actual CapsuleCollider. Call again if the
    /// capsule changes at runtime (for example if you add crouch later).
    /// </summary>
    public void RefreshGroundProbe()
    {
        if (playerRoot == null) return;

        playerCapsule = playerRoot.GetComponent<CapsuleCollider>();

        if (playerCapsule != null)
        {
            // Do NOT assume the root sits at the feet - in this project one player capsule is
            // centred on the root (feet 1m below it) and another has its centre raised so the
            // root is at the feet. Deriving from the collider handles both.
            probeRadius = playerCapsule.radius * groundProbeRadiusScale;

            float halfHeight = Mathf.Max(playerCapsule.height * 0.5f, playerCapsule.radius);
            float bottomCapCentreY = playerCapsule.center.y - (halfHeight - playerCapsule.radius);

            probeLocalOrigin = new Vector3(playerCapsule.center.x, bottomCapCentreY, playerCapsule.center.z);

            // The smaller probe sphere's underside sits this far above the capsule's own bottom.
            probeSlack = playerCapsule.radius - probeRadius;
        }
        else
        {
            probeRadius = fallbackProbeRadius;
            probeLocalOrigin = new Vector3(0f, fallbackFeetLocalY + probeRadius, 0f);
            probeSlack = 0f;
        }
    }

    private void OnDisable()
    {
        // Leave the camera exactly at rest, so anything that takes this transform over
        // (focusing_mechanic, cutscenes) does not inherit a frozen bob offset.
        transform.localPosition = basePos;
        transform.localRotation = baseRot;
    }

    // Runs after Update (mouse look writes camera_holder there) and after physics
    // interpolation has placed the Rigidbody for this frame, so we read final values
    // and nothing overwrites us afterwards.
    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        dt = Mathf.Min(dt, 0.1f);   // hitch clamp, so one long frame cannot jolt the springs

        // While the movement script is off, focusing_mechanic / ispect_mechanic are driving the
        // camera in world space. Writing localPosition here would fight them, so do nothing at
        // all and reset on resume.
        if (movementScript != null && !movementScript.enabled)
        {
            suspended = true;
            lastRootPos = playerRoot.position;
            return;
        }

        if (suspended)
        {
            suspended = false;
            measuredSpeed = 0f;
            speedFactor = 0f;
            springPos = 0f;
            springVel = 0f;
            swayOffset = Vector3.zero;
            lastRootPos = playerRoot.position;
        }

        SampleMovement(dt);
        UpdateGrounded(dt);
        UpdateGait(dt);
        IntegrateSpring(dt);
        UpdateBreathing(dt);
        UpdateSway(dt);
        ApplyToTransform();
    }

    /// <summary>Push the camera. Negative dips down. Call from scares, damage, impacts.</summary>
    public void AddImpulse(float amount)
    {
        springVel -= amount;
    }

    // Measured displacement, not rb.velocity: MovePosition on a non-kinematic body does not
    // produce a reliable velocity. This also means the bob correctly stops dead when you
    // walk into a wall - input is still held, but nothing is moving.
    private void SampleMovement(float dt)
    {
        Vector3 pos = playerRoot.position;
        Vector3 delta = pos - lastRootPos;
        lastRootPos = pos;

        float rawVertical = delta.y / dt;

        delta.y = 0f;
        float rawHorizontal = delta.magnitude / dt;

        // One huge frame delta is a teleport (focus mechanic, parkour warp), not running.
        if (rawHorizontal > teleportRejectSpeed || Mathf.Abs(rawVertical) > teleportRejectSpeed * 2f)
        {
            rawHorizontal = 0f;
            rawVertical = 0f;
        }

        verticalSpeed = rawVertical;

        // Exponential smoothing: framerate independent, unlike Lerp(a, b, k * dt).
        measuredSpeed = Mathf.Lerp(measuredSpeed, rawHorizontal, 1f - Mathf.Exp(-speedSmoothing * dt));
    }

    private void UpdateGrounded(float dt)
    {
        bool wasGrounded = isGrounded;

        Vector3 origin = playerRoot.TransformPoint(probeLocalOrigin);
        float castDistance = probeSlack + groundCheckDistance;

        isGrounded = false;
        if (castDistance > 0f && probeRadius > 0f)
        {
            int count = Physics.SphereCastNonAlloc(origin, probeRadius, Vector3.down,
                groundHits, castDistance, groundMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = groundHits[i].collider;
                if (hit == null) continue;

                // Skip our own capsule. Everything is on the Default layer in this project,
                // so a layer mask alone will not exclude the player.
                Transform t = hit.transform;
                if (t == playerRoot || t.IsChildOf(playerRoot)) continue;

                isGrounded = true;
                break;
            }
        }

        if (landingCooldown > 0f) landingCooldown -= dt;

        if (isGrounded && !wasGrounded && landingCooldown <= 0f)
        {
            float fallSpeed = Mathf.Max(0f, -verticalSpeed);
            float strength = Mathf.Clamp01(fallSpeed / Mathf.Max(landReferenceFallSpeed, 0.01f));

            if (strength > 0.02f)
            {
                AddImpulse(landImpulse * strength);
                landingCooldown = landingDebounce;
            }
        }
    }

    private void UpdateGait(float dt)
    {
        groundedBlend = Mathf.MoveTowards(groundedBlend, isGrounded ? 1f : 0f,
            dt / Mathf.Max(airborneFadeTime, 0.001f));

        float normalised = referenceSpeed > 0.01f ? measuredSpeed / referenceSpeed : 0f;
        speedFactor = Mathf.Clamp01(normalised) * groundedBlend;

        // Accumulate the phase instead of using Time.time * frequency. Changing the
        // frequency then changes the RATE of the phase and never its VALUE, so the
        // walk -> sprint transition cannot pop.
        float advance = dt * TwoPi * strideFrequency * speedFactor;
        advance = Mathf.Min(advance, TwoPi * 0.9f);   // never skip a whole stride in one frame

        float previousPhase = bobPhase;
        bobPhase += advance;

        // Vertical is -cos(2 * phase), so its troughs - the footfalls - are at phase
        // PI and 2PI. Two footfalls per stride.
        if (Crossed(previousPhase, bobPhase, Mathf.PI)) Footfall();
        if (Crossed(previousPhase, bobPhase, TwoPi)) Footfall();

        if (bobPhase >= TwoPi) bobPhase -= TwoPi;
    }

    private static bool Crossed(float from, float to, float target)
    {
        return from < target && to >= target;
    }

    private void Footfall()
    {
        if (speedFactor <= footstepMinSpeedFactor) return;

        AddImpulse(footstepImpulse * speedFactor);
        OnFootstep?.Invoke(speedFactor);
    }

    // Substepped because this stiffness is unstable in a single large step - at 30 fps a
    // lone dt of 0.033 would oscillate or diverge.
    private void IntegrateSpring(float dt)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / SpringSubstep), 1, 8);
        float sdt = dt / steps;

        for (int i = 0; i < steps; i++)
        {
            springVel += (-springPos * stiffness - springVel * damping) * sdt;
            springPos += springVel * sdt;
        }
    }

    private void UpdateBreathing(float dt)
    {
        breathPhase += dt * TwoPi * breathFrequency;
        if (breathPhase >= TwoPi) breathPhase -= TwoPi;
    }

    private void UpdateSway(float dt)
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        Vector3 target = new Vector3(-mouseX * swayAmount, -mouseY * swayAmount, 0f);
        if (target.sqrMagnitude > swayMaxOffset * swayMaxOffset)
        {
            target = target.normalized * swayMaxOffset;
        }

        swayOffset = Vector3.Lerp(swayOffset, target, 1f - Mathf.Exp(-swaySmoothing * dt));
    }

    private void ApplyToTransform()
    {
        // Unit gait curves. Lateral peaks at mid-stance, vertical troughs at footfall.
        float lateralUnit = Mathf.Sin(bobPhase);
        float verticalUnit = -Mathf.Cos(bobPhase * 2f);

        float lateral = lateralUnit * lateralAmount * speedFactor;
        float vertical = verticalUnit * verticalAmount * speedFactor;

        // Cross-faded against the gait, so the camera is never perfectly static
        // and never doubly animated.
        float breath = Mathf.Sin(breathPhase) * breathAmount * (1f - speedFactor);

        float roll = -lateralUnit * rollAmount * speedFactor;   // lean into the sway
        float nod = -verticalUnit * nodAmount * speedFactor;     // head drops at footfall

        Vector3 offset = new Vector3(
            lateral + swayOffset.x,
            vertical + breath + springPos + swayOffset.y,
            0f);

        // The parent (camera_holder) carries look pitch, so our local space is pitched. Without
        // this, looking straight down rotates "up" into "forward" and the vertical bob reads as
        // a forward/back dolly instead. The parent's local rotation is pure X, so the inverse
        // is exact.
        if (pitchIndependentTranslation && transform.parent != null)
        {
            offset = Quaternion.Inverse(transform.parent.localRotation) * offset;
        }

        // Absolute write of base + offset. Never read the current transform and add to it,
        // or the offset accumulates and rest position drifts.
        transform.localPosition = basePos + offset;
        transform.localRotation = baseRot * Quaternion.Euler(nod, 0f, roll);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (playerRoot == null)
        {
            Rigidbody body = GetComponentInParent<Rigidbody>();
            if (body != null) playerRoot = body.transform;
        }
        if (playerRoot == null) return;

        // Recompute so the gizmo is correct in the editor before Awake has ever run.
        if (!Application.isPlaying) RefreshGroundProbe();

        Vector3 origin = playerRoot.TransformPoint(probeLocalOrigin);
        float castDistance = probeSlack + groundCheckDistance;

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(origin, probeRadius);
        Gizmos.DrawWireSphere(origin + Vector3.down * castDistance, probeRadius);
    }
#endif
}

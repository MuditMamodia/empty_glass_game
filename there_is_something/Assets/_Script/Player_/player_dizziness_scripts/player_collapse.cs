using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// First-person collapse: the low-blood-pressure stagger, the room spinning, the knees folding,
/// and the head landing on the floor. Then getting back up on shaking legs.
///
/// Setup: drop this on the PLAYER root (the object with the Rigidbody and first_Person_Movement).
/// That is all. It finds the camera holder through first_Person_Movement.CameraHolder, and it
/// works out how far the head has to fall from the CapsuleCollider, so there is nothing to type in.
///
///     player_collapse.Instance.PlayCollapse();      // ~10s, ends lying on the floor
///     player_collapse.Instance.PlayGetUp();         // ~10s, ends standing, control returned
///     player_collapse.Instance.ResetInstant();      // snap back to standing
///
/// The body does NOT ragdoll. The capsule stays upright exactly where it was and the CAMERA does
/// the falling, which is how first-person collapses are done everywhere: a real ragdoll needs the
/// collider to lie down, and a 2m capsule going horizontal in a corridor shoves itself through a
/// wall or wedges in the geometry. Moving the head cannot get stuck, cannot clip, and reverses
/// perfectly.
///
/// WHAT IT WRITES, AND WHY THAT TRANSFORM
///
/// camera_holder's localPosition and localRotation, and nothing else. In this rig there are three
/// transforms and two of them are already owned:
///
///     Player  ...... first_Person_Movement writes rotation absolutely, every Update
///     camera_holder  first_Person_Movement writes localRotation absolutely, every Update
///     Main Camera .. Head_bob_system writes localPosition AND localRotation, every LateUpdate
///
/// Every one of those is an absolute write, so writing the same transform from here would simply
/// be overwritten later in the same frame. The answer is the one the project already uses for
/// focusing_mechanic: disable first_Person_Movement. That frees camera_holder, stops mouse-look
/// and movement in one go, and Head_bob_system watches that exact flag and suspends itself rather
/// than fighting whoever has taken the camera. Nothing has to be hacked.
///
/// HANDING THE CAMERA BACK WITHOUT A POP
///
/// Mouse-look keeps its own pitch accumulator and writes head.localRotation = Euler(xRotation,0,0)
/// from it. Re-enabling it while the head is at any other pitch snaps the view in one frame. So
/// the pitch is read off the transform the moment control is taken - which is exact, because
/// mouse-look's write is the most recent thing to have touched it - and the get-up finishes on
/// that same pitch. The accumulator cannot have drifted in between, because its Update was not
/// running. Zero pop, and nothing in first_Person_Movement had to change.
/// </summary>
[DisallowMultipleComponent]
public class player_collapse : MonoBehaviour
{
    [Header("References - both auto-resolve, leave them empty")]
    [Tooltip("Disabled for the whole collapse. Leave empty to take it from this GameObject.")]
    [SerializeField] private first_Person_Movement movement;
    [Tooltip("The transform that gets moved - camera_holder. Leave empty to take it from the " +
             "movement script's Camera Holder.")]
    [SerializeField] private Transform head;

    [Header("Timing")]
    [Tooltip("Length of PlayCollapse. 10 matches eye_effect's faint, so the lids finish closing " +
             "as the head hits the floor.")]
    [SerializeField] private float collapseDuration = 10f;
    [Tooltip("Length of PlayGetUp. 10 matches eye_effect's wake-up.")]
    [SerializeField] private float getUpDuration = 10f;
    [Tooltip("On by default, to match eye_effect: a faint often runs while the game is paused or " +
             "slowed, and scaled time would stall the animation.")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("How far the head falls")]
    [Tooltip("Height of the head above the feet once he is lying on his side. The drop distance " +
             "is worked out from this and the CapsuleCollider, so it adapts to any player height.")]
    [SerializeField] private float lyingHeadHeight = 0.22f;
    [Tooltip("Used only if there is no CapsuleCollider to measure.")]
    [SerializeField] private float fallbackDropDistance = 1.5f;

    [Header("Spin - the dizzy stagger before the fall")]
    [Tooltip("Fraction of the collapse spent staggering before the knees actually go.")]
    [SerializeField, Range(0.1f, 0.9f)] private float spinPhaseEnd = 0.52f;
    [Tooltip("Rotations per second of the spin at the start. Slow is far more sickening than fast.")]
    [SerializeField, Range(0.02f, 2f)] private float spinSpeedStart = 0.16f;
    [SerializeField, Range(0.02f, 2f)] private float spinSpeedEnd = 0.5f;
    [Tooltip("Degrees the look direction swings left and right as the room goes round.")]
    [SerializeField, Range(0f, 40f)] private float spinYaw = 9f;
    [SerializeField, Range(0f, 40f)] private float spinPitch = 6f;
    [Tooltip("Degrees of head tilt. This is the one that reads as vertigo - raise it first.")]
    [SerializeField, Range(0f, 40f)] private float spinRoll = 8f;
    [Tooltip("Metres the head wanders as he fails to stand still.")]
    [SerializeField, Range(0f, 0.4f)] private float spinDrift = 0.07f;
    [Tooltip("A faster, smaller shake on top of the spin, so it never reads as a clean sine wave.")]
    [SerializeField, Range(0f, 1f)] private float tremor = 0.35f;

    [Header("The fall")]
    [Tooltip("Fraction of the collapse at which the head reaches the floor. The rest is the " +
             "impact and the settle.")]
    [SerializeField, Range(0.3f, 0.98f)] private float fallPhaseEnd = 0.8f;
    [Tooltip("1 falls to the right, -1 to the left, 0 picks one at random each time.")]
    [SerializeField, Range(-1, 1)] private int fallSide;
    [Tooltip("Head tilt at the moment of landing. Around 75 reads as going down sideways.")]
    [SerializeField, Range(0f, 110f)] private float fallRoll = 74f;
    [Tooltip("Positive pitches the look DOWN towards the floor on the way there.")]
    [SerializeField, Range(-60f, 60f)] private float fallPitch = 32f;
    [SerializeField, Range(-40f, 40f)] private float fallYaw = 16f;
    [Tooltip("Metres the head travels sideways. You do not fall straight down.")]
    [SerializeField, Range(0f, 1.5f)] private float fallSideways = 0.42f;
    [SerializeField, Range(-1.5f, 1.5f)] private float fallForward = 0.26f;

    [Header("Impact")]
    [Tooltip("How hard the head bounces off the floor. 0 lands with no jolt at all.")]
    [SerializeField, Range(0f, 8f)] private float impactStrength = 3f;
    [SerializeField] private float impactStiffness = 150f;
    [SerializeField] private float impactDamping = 14f;

    [Header("Lying on the floor")]
    [Tooltip("The pose he comes to rest in, after the bounce has died away.")]
    [SerializeField, Range(0f, 110f)] private float restRoll = 78f;
    [Tooltip("Slightly negative looks a little UP along the floor, which is what you see when " +
             "your cheek is on the ground.")]
    [SerializeField, Range(-60f, 60f)] private float restPitch = -6f;
    [SerializeField, Range(-40f, 40f)] private float restYaw = 12f;
    [Tooltip("Metres the head rises and falls with each breath while he is out. Without this the " +
             "camera is perfectly frozen, which reads as the game having hung.")]
    [SerializeField, Range(0f, 0.08f)] private float breathAmount = 0.014f;
    [SerializeField, Range(0.05f, 2f)] private float breathSpeed = 0.33f;

    [Header("Getting up (normalised time -> how far up he is)")]
    [Tooltip("Holds down, props up on an elbow, slips, makes it to his knees, nearly goes again, " +
             "then stands. Deliberately dipping where eye_effect's wake-up winces.")]
    [SerializeField] private AnimationCurve getUpRise = BuildRiseCurve();
    [Tooltip("Degrees of sway on the way up, fading as he steadies.")]
    [SerializeField, Range(0f, 30f)] private float getUpWobble = 7f;

#if UNITY_EDITOR
    [Header("Demo (editor only)")]
    [Tooltip("Tick while playing to run the collapse. It unticks itself.")]
    [SerializeField] private bool collapseNow;
    [Tooltip("Tick while playing to get back up. It unticks itself.")]
    [SerializeField] private bool getUpNow;
    [Tooltip("Tick to snap straight back to standing with control returned.")]
    [SerializeField] private bool resetNow;
#endif

    /// <summary>
    /// Shared instance. Unlike eye_effect this cannot be invented on demand - it has to live on
    /// the player, next to the Rigidbody and the camera - so this returns null if there is none
    /// in the scene, with a warning saying so.
    /// </summary>
    public static player_collapse Instance
    {
        get
        {
            if (instance != null) return instance;

            instance = FindAnyObjectByType<player_collapse>(FindObjectsInactive.Exclude);

            // Said once, not once per frame: this is safe to read from an Update loop, and a
            // warning that repeats sixty times a second buries everything else in the Console.
            if (instance == null && !warnedMissing)
            {
                warnedMissing = true;
                Debug.LogWarning("player_collapse.Instance: there is no player_collapse in the " +
                                 "scene, so the fall cannot play. Add the component to the Player.");
            }

            return instance;
        }
    }

    /// <summary>True while PlayCollapse or PlayGetUp is animating.</summary>
    public bool IsPlaying => routine != null;

    /// <summary>True from the moment he hits the floor until PlayGetUp finishes.</summary>
    public bool IsDown => down;

    /// <summary>True whenever this script is holding the camera and mouse-look is switched off.</summary>
    public bool HasControl => controlTaken;

    /// <summary>0 standing, 1 flat on the floor. Drive muffled audio or a heartbeat off it.</summary>
    public float FallProgress => dropDistance > 0.01f
        ? Mathf.Clamp01(-headOffset.y / dropDistance)
        : 0f;

    private static player_collapse instance;
    private static bool warnedMissing;

    // The animated state. One set of numbers; Commit pushes them to the transform.
    private Vector3 headOffset;
    private float pitch;
    private float yaw;
    private float roll;

    // Captured when control is taken, restored when it is handed back.
    private Vector3 standPos;
    private float standPitch;

    private float dropDistance;
    private int lastSide = 1;

    private float springPos;
    private float springVel;

    private Coroutine routine;
    private Coroutine holdRoutine;
    private Rigidbody body;
    private bool controlTaken;
    private bool down;

    private const float SpringSubstep = 0.005f;
    private const float TwoPi = Mathf.PI * 2f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("player_collapse on '" + name + "': another instance is already " +
                             "active on '" + instance.name + "'. The newest one now answers " +
                             "player_collapse.Instance.", this);
        }
        instance = this;

        if (movement == null) movement = GetComponent<first_Person_Movement>();
        if (movement == null) movement = GetComponentInParent<first_Person_Movement>();
        if (head == null && movement != null) head = movement.CameraHolder;

        if (head == null)
        {
            Debug.LogError("player_collapse on '" + name + "': no Head assigned, and no camera " +
                           "holder could be found through first_Person_Movement, so there is " +
                           "nothing to move. Disabling. Put this component on the Player root, " +
                           "or drag camera_holder into Head.", this);
            enabled = false;
            return;
        }

        if (movement == null)
        {
            Debug.LogWarning("player_collapse on '" + name + "': no first_Person_Movement found, " +
                             "so the player will keep walking and looking around during the " +
                             "collapse. Put this component on the Player root.", this);
        }

        body = GetComponent<Rigidbody>();
        if (body == null) body = GetComponentInParent<Rigidbody>();

        Sanitise();
        ResolveDropDistance();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (collapseNow)
        {
            collapseNow = false;
            PlayCollapse();
        }

        if (getUpNow)
        {
            getUpNow = false;
            PlayGetUp();
        }

        if (resetNow)
        {
            resetNow = false;
            ResetInstant();
        }
    }
#endif

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Going down. The room starts turning and he cannot hold still, the knees nearly give out
    /// twice, then they fold and the floor comes up. Ends lying on his side, breathing, with
    /// mouse-look and movement still switched off - call PlayGetUp or ResetInstant to give them
    /// back.
    /// </summary>
    public Coroutine PlayCollapse(Action onComplete = null)
    {
        return PlayCollapse(collapseDuration, onComplete);
    }

    /// <inheritdoc cref="PlayCollapse(Action)"/>
    public Coroutine PlayCollapse(float duration, Action onComplete = null)
    {
        return Play(CollapseRoutine(Mathf.Max(0.1f, duration), onComplete));
    }

    /// <summary>
    /// Getting back up, from wherever the head currently is. He props himself up, slips back,
    /// makes it to his knees, nearly goes down again, then stands - swaying the whole way and
    /// steadying as he arrives. Hands control back to mouse-look at the exact pitch it was
    /// holding, so the view does not jump.
    /// </summary>
    public Coroutine PlayGetUp(Action onComplete = null)
    {
        return PlayGetUp(getUpDuration, onComplete);
    }

    /// <inheritdoc cref="PlayGetUp(Action)"/>
    public Coroutine PlayGetUp(float duration, Action onComplete = null)
    {
        return Play(GetUpRoutine(Mathf.Max(0.1f, duration), onComplete));
    }

    /// <summary>
    /// Cancels whatever is playing and leaves the head exactly where it is. Control is NOT handed
    /// back - use ResetInstant for that.
    /// </summary>
    public void Stop()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        StopHold();
    }

    /// <summary>
    /// Snaps back to standing with no animation and gives mouse-look and movement back. Use it
    /// across a scene load, or to get out of a collapse you triggered by accident.
    /// </summary>
    public void ResetInstant()
    {
        Stop();

        headOffset = Vector3.zero;
        pitch = 0f;
        yaw = 0f;
        roll = 0f;
        springPos = 0f;
        springVel = 0f;
        down = false;

        if (controlTaken) ReleaseControl();
    }

    // ------------------------------------------------------------------ driving

    private Coroutine Play(IEnumerator bodyRoutine)
    {
        Stop();

        if (!isActiveAndEnabled)
        {
            Debug.LogError("player_collapse on '" + name + "': the component is disabled, so the " +
                           "collapse cannot run. Enable the GameObject before calling.", this);
            return null;
        }

        routine = StartCoroutine(bodyRoutine);
        return routine;
    }

    private IEnumerator CollapseRoutine(float duration, Action onComplete)
    {
        TakeControl();

        int side = ResolveSide();
        float t = 0f;
        float spin = 0f;
        bool landed = false;

        springPos = 0f;
        springVel = 0f;

        while (t < 1f)
        {
            float dt = Dt();
            t = Mathf.Min(1f, t + dt / duration);

            // --- the room going round ------------------------------------------------------
            // Amplitude ramps in rather than starting at full tilt, so the first second reads as
            // "something is wrong" instead of an instant camera effect.
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, spinPhaseEnd, t));

            // Phase is accumulated, not Time.time * speed. That way speeding the spin up changes
            // the RATE and never the VALUE, so it cannot jump as it accelerates.
            spin += dt * Mathf.Lerp(spinSpeedStart, spinSpeedEnd, rise);
            float ang = spin * TwoPi;

            // Yaw and pitch a quarter-turn apart trace a circle rather than a flat wobble - the
            // look direction actually orbits, which is what being spun reads as. Roll is offset
            // again so the tilt lags the turn, the way a head does.
            float orbitYaw = Mathf.Sin(ang) * spinYaw * rise;
            float orbitPitch = Mathf.Cos(ang) * spinPitch * rise;
            float orbitRoll = Mathf.Sin(ang + 0.9f) * spinRoll * rise;

            // A faster, smaller layer on detuned multiples, so the motion never repeats on an
            // obvious beat and never looks like a clean sine.
            float shake = tremor * rise;
            orbitYaw += Mathf.Sin(ang * 2.7f + 0.4f) * spinYaw * 0.3f * shake;
            orbitPitch += Mathf.Sin(ang * 3.3f + 1.7f) * spinPitch * 0.3f * shake;
            orbitRoll += Mathf.Sin(ang * 2.1f + 2.4f) * spinRoll * 0.35f * shake;

            float driftX = Mathf.Sin(ang * 0.7f) * spinDrift * rise;
            float driftZ = Mathf.Sin(ang * 0.53f + 1.2f) * spinDrift * 0.6f * rise;

            // The knees nearly go twice before they actually do. Two short dips the head drops
            // into and recovers from - this is the beat that makes it read as a body failing
            // rather than as a camera being animated downwards.
            float stumble = Bump(t, spinPhaseEnd * 0.56f, 0.03f) * 0.16f +
                            Bump(t, spinPhaseEnd * 0.85f, 0.034f) * 0.27f;

            // --- the fall ------------------------------------------------------------------
            float fall = Mathf.InverseLerp(spinPhaseEnd, fallPhaseEnd, t);
            float fold = Fold(fall);

            if (!landed && fall >= 1f)
            {
                landed = true;
                springVel = -impactStrength;   // the head hits the floor
            }

            if (landed) IntegrateSpring(dt);

            // --- coming to rest -------------------------------------------------------------
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fallPhaseEnd, 1f, t));

            // Blend the three poses in order: staggering -> going down -> lying still. Lerping
            // through fold and then through settle means each stage only has to describe its own
            // target pose, and the transitions take care of themselves.
            pitch = Mathf.Lerp(Mathf.Lerp(orbitPitch, fallPitch, fold), restPitch, settle);
            yaw = Mathf.Lerp(Mathf.Lerp(orbitYaw, fallYaw * side, fold), restYaw * side, settle);
            roll = Mathf.Lerp(Mathf.Lerp(orbitRoll, fallRoll * side, fold), restRoll * side, settle);

            // The impact shows in the neck, not just in the height.
            pitch += springPos * 45f;
            roll += springPos * 22f * side;

            // Sideways and roll must share a sign. Quaternion.Euler(0,0,+z) rotates the camera's
            // up vector towards +X, so a positive roll tilts the head to the RIGHT - and a man
            // going down to his right has to travel right with it, or he reads as tipping one way
            // while sliding the other.
            headOffset = new Vector3(
                driftX * (1f - fold) + fallSideways * side * fold,
                -(fold * dropDistance + stumble * (1f - fold)) + springPos * 0.25f,
                driftZ * (1f - fold) + fallForward * fold);

            Commit();
            yield return null;
        }

        down = true;
        routine = null;

        // Hold the pose and keep breathing. A camera that stops dead the instant the coroutine
        // ends reads as a freeze, not as a body on the floor.
        holdRoutine = StartCoroutine(LyingIdle());

        onComplete?.Invoke();
    }

    private IEnumerator LyingIdle()
    {
        float phase = 0f;

        while (true)
        {
            float dt = Dt();
            phase += dt * breathSpeed * TwoPi;
            if (phase > TwoPi) phase -= TwoPi;

            float breath = Mathf.Sin(phase);

            pitch = restPitch + breath * 0.8f;
            yaw = restYaw * lastSide + breath * 0.5f;
            roll = restRoll * lastSide;

            headOffset = new Vector3(
                fallSideways * lastSide,
                -dropDistance + breath * breathAmount,
                fallForward);

            Commit();
            yield return null;
        }
    }

    private IEnumerator GetUpRoutine(float duration, Action onComplete)
    {
        StopHold();

        // Taken rather than assumed: PlayGetUp is allowed to be called from a standing start, or
        // from halfway through a collapse that was cut short, and it has to begin from wherever
        // the head actually is or it snaps on the first frame.
        TakeControl();

        float fromPitch = pitch;
        float fromYaw = yaw;
        float fromRoll = roll;
        Vector3 fromOffset = headOffset;

        float t = 0f;
        float wobblePhase = 0f;

        while (t < 1f)
        {
            float dt = Dt();
            t = Mathf.Min(1f, t + dt / duration);

            float riseAmount = Evaluate(getUpRise, t, t);

            // Still unsteady the whole way up, settling as he arrives. The sway also slows down,
            // so the last second is a long lean rather than a shiver.
            wobblePhase += dt * Mathf.Lerp(0.9f, 0.3f, t) * TwoPi;
            float unsteady = Mathf.Pow(1f - t, 1.3f);

            float wobbleRoll = Mathf.Sin(wobblePhase) * getUpWobble * unsteady;
            float wobblePitch = Mathf.Cos(wobblePhase * 0.8f) * getUpWobble * 0.55f * unsteady;
            float wobbleYaw = Mathf.Sin(wobblePhase * 0.6f + 1f) * getUpWobble * 0.7f * unsteady;

            pitch = Mathf.Lerp(fromPitch, 0f, riseAmount) + wobblePitch;
            yaw = Mathf.Lerp(fromYaw, 0f, riseAmount) + wobbleYaw;
            roll = Mathf.Lerp(fromRoll, 0f, riseAmount) + wobbleRoll;

            headOffset = Vector3.Lerp(fromOffset, Vector3.zero, riseAmount) +
                         new Vector3(Mathf.Sin(wobblePhase * 0.7f) * 0.035f * unsteady, 0f, 0f);

            Commit();
            yield return null;
        }

        // Land on the exact rest pose rather than wherever the last tick happened to be.
        // Deliberately not ResetInstant: that calls Stop(), and a coroutine must not stop itself.
        headOffset = Vector3.zero;
        pitch = 0f;
        yaw = 0f;
        roll = 0f;
        springPos = 0f;
        springVel = 0f;
        down = false;

        ReleaseControl();

        routine = null;
        onComplete?.Invoke();
    }

    // ------------------------------------------------------------------ control handover

    private void TakeControl()
    {
        if (controlTaken) return;
        controlTaken = true;

        // Captured here and only here, so a second call mid-collapse cannot record a falling head
        // as the standing pose and strand the camera on the floor forever.
        standPos = head.localPosition;
        standPitch = Unwrap(head.localEulerAngles.x);

        // This one line does three things: stops movement, stops mouse-look, and makes
        // Head_bob_system suspend itself - it watches this exact flag.
        if (movement != null) movement.enabled = false;

        // MovePosition on a non-kinematic body leaves residual velocity behind, so without this
        // he keeps sliding across the floor after his legs have given out.
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    private void ReleaseControl()
    {
        if (!controlTaken) return;
        controlTaken = false;

        // Exactly the pitch mouse-look is still holding in its accumulator, so its first write
        // after being re-enabled changes nothing and the view does not jump.
        head.localPosition = standPos;
        head.localRotation = Quaternion.Euler(standPitch, 0f, 0f);

        if (movement != null) movement.enabled = true;
    }

    private void StopHold()
    {
        if (holdRoutine == null) return;

        StopCoroutine(holdRoutine);
        holdRoutine = null;
    }

    // Pushes this frame's numbers onto the transform. Absolute writes of base + offset - never
    // read the transform back and add to it, or the offset accumulates and the rest pose drifts.
    private void Commit()
    {
        head.localPosition = standPos + headOffset;
        head.localRotation = Quaternion.Euler(standPitch + pitch, yaw, roll);
    }

    // ------------------------------------------------------------------ setup

    // Field initialisers only apply to fields Unity did not find in the scene file, so a component
    // that has been through a few script edits can be holding zeros the Inspector makes look
    // deliberate. A zero duration produces a collapse that runs perfectly and shows nothing.
    private void Sanitise()
    {
        if (collapseDuration < 0.1f) collapseDuration = 10f;
        if (getUpDuration < 0.1f) getUpDuration = 10f;
        if (spinPhaseEnd < 0.1f || spinPhaseEnd > 0.9f) spinPhaseEnd = 0.52f;
        if (fallPhaseEnd <= spinPhaseEnd) fallPhaseEnd = Mathf.Min(0.98f, spinPhaseEnd + 0.28f);
        if (spinSpeedStart < 0.02f) spinSpeedStart = 0.16f;
        if (spinSpeedEnd < 0.02f) spinSpeedEnd = 0.5f;
        if (breathSpeed < 0.05f) breathSpeed = 0.33f;
        if (impactStiffness < 1f) impactStiffness = 150f;
        if (impactDamping < 0.1f) impactDamping = 14f;

        if (getUpRise == null || getUpRise.length == 0) getUpRise = BuildRiseCurve();
    }

    // Derived from the collider rather than typed in, because the root is not necessarily at the
    // feet - in this project the capsule is centred on the root, so the feet are a metre BELOW it.
    // Measuring handles that and any other rig without a number to keep in sync.
    private void ResolveDropDistance()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule == null) capsule = GetComponentInParent<CapsuleCollider>();

        if (capsule != null)
        {
            float halfHeight = Mathf.Max(capsule.height * 0.5f, capsule.radius);
            float feetLocalY = capsule.center.y - halfHeight;

            dropDistance = Mathf.Max(0.2f, head.localPosition.y - (feetLocalY + lyingHeadHeight));
            return;
        }

        dropDistance = Mathf.Max(0.2f, fallbackDropDistance);
        Debug.LogWarning("player_collapse on '" + name + "': no CapsuleCollider found, so the " +
                         "fall distance could not be measured. Using Fallback Drop Distance (" +
                         dropDistance + "m).", this);
    }

    // ------------------------------------------------------------------ maths

    private float Dt()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        return Mathf.Min(dt, 0.1f);   // one long frame must not skip half the collapse
    }

    private int ResolveSide()
    {
        if (fallSide > 0) lastSide = 1;
        else if (fallSide < 0) lastSide = -1;
        else lastSide = UnityEngine.Random.value < 0.5f ? -1 : 1;

        return lastSide;
    }

    // Substepped because this stiffness is unstable in a single large step - at 30 fps a lone
    // dt of 0.033 would oscillate or diverge instead of damping out.
    private void IntegrateSpring(float dt)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / SpringSubstep), 1, 8);
        float sdt = dt / steps;

        for (int i = 0; i < steps; i++)
        {
            springVel += (-springPos * impactStiffness - springVel * impactDamping) * sdt;
            springPos += springVel * sdt;
        }
    }

    // The shape of a body giving up. The knees fold slowly through the first quarter and only
    // cover a sixth of the distance; after that nothing is holding him and he accelerates like a
    // dropped weight. A straight line here reads as a lift descending, not as a collapse.
    private static float Fold(float f)
    {
        f = Mathf.Clamp01(f);
        if (f <= 0f) return 0f;

        const float knee = 0.28f;
        const float kneeTravel = 0.17f;

        if (f < knee) return f / knee * kneeTravel;

        float after = (f - knee) / (1f - knee);
        return kneeTravel + (1f - kneeTravel) * Mathf.Pow(after, 1.75f);
    }

    /// <summary>A gaussian pulse: 0 away from the centre, 1 at it. Used for the stumbles.</summary>
    private static float Bump(float t, float centre, float width)
    {
        float x = (t - centre) / Mathf.Max(0.0001f, width);
        return Mathf.Exp(-x * x);
    }

    // localEulerAngles comes back in 0..360, so a pitch of -30 reads as 330. Handing that to
    // mouse-look's clamp of -90..90 would peg the view at 90 and stare at the floor.
    private static float Unwrap(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        else if (degrees < -180f) degrees += 360f;

        return degrees;
    }

    private static float Evaluate(AnimationCurve curve, float t, float fallback)
    {
        if (curve == null || curve.length == 0) return Mathf.Clamp01(fallback);
        return Mathf.Clamp01(curve.Evaluate(t));
    }

    // Keyframes default to flat tangents, which eases in and out between every pair - the right
    // feel for a body fighting its own weight, and it makes close-together keys read as a slip.
    private static AnimationCurve BuildRiseCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.14f, 0f),      // still out cold
            new Keyframe(0.26f, 0.22f),   // props himself up on an elbow
            new Keyframe(0.34f, 0.16f),   // and it slips
            new Keyframe(0.48f, 0.55f),   // onto his knees
            new Keyframe(0.58f, 0.44f),   // nearly goes down again
            new Keyframe(0.76f, 0.88f),
            new Keyframe(0.86f, 0.8f),    // one last sway
            new Keyframe(1f, 1f));
    }
}

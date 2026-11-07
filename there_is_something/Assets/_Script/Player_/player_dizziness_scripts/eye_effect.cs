using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Full-screen human-eye effect with two playable states: fainting, and waking up with a headache.
///
/// Setup: drop this component on the "eyeeffectCanvas" GameObject. That is all. It builds its own
/// child image inside that canvas at runtime, and its own URP volume, so there is nothing to wire
/// up and nothing to prefab. If the component sits somewhere else instead (the Player, a manager
/// object) it looks the canvas up by name, so either arrangement works.
///
///     eye_effect.Instance.PlayFaint();                        // ~10s, ends holding black
///     eye_effect.Instance.PlayWakeUp();                       // ~10s, starts black, ends clear
///     eye_effect.Instance.PlayFaint(6f, () => LoadScene());   // custom length + callback
///     eye_effect.Instance.SetOpenInstant(0f);                 // snap shut, e.g. across a load
///
/// There are two layers to it, and the realism comes from them running against each other:
///
///  1. The EYE. Lids and tunnel vision, rasterised into a small Texture2D (256x144) and stretched
///     over the screen. Bilinear upscaling is what makes the lid edges and the vignette soft, so
///     the tiny buffer costs almost nothing and still looks right at 4K. Drawn through a RawImage
///     on eyeeffectCanvas, and/or through OnGUI - see Draw Path.
///
///  2. The WORLD. A URP Volume created at runtime, driven per frame: depth of field for the blur,
///     colour grading for the greyout and the light flooding in, chromatic aberration for the
///     double vision, lens distortion for the swim, film grain for the hypoxic noise. This is the
///     part that blurs what you are looking at - an overlay cannot do that, because it has no
///     access to what is underneath it.
///
/// The order matters and is deliberate. A real faint is a GREYOUT before it is a blackout: the
/// world softens, the colour drains and the edges smear while your eyes are still open, and only
/// then do the lids go. Waking is the reverse and worse - light hurts, focus does not come back
/// smoothly but hunts and overshoots, and every heartbeat squeezes the picture.
///
/// Nothing here needs a custom shader, a material, a sprite or a renderer feature. The overlay is
/// pipeline independent; the post-processing needs URP, and switches itself off with a warning if
/// it cannot find it.
///
/// The effect never blocks input and never touches the camera transform, so it composes with
/// first_Person_Movement and Head_bob_system instead of fighting them. Drive your own camera roll
/// or muffled audio off the Openness / Intensity / Blur properties if you want more.
/// </summary>
[DisallowMultipleComponent]
public class eye_effect : MonoBehaviour
{
    /// <summary>Which of the two renderers puts the eye on screen.</summary>
    public enum DrawPath
    {
        /// <summary>
        /// Belt and braces: both at once. Guaranteed visible, but the overlay is composited twice
        /// so it reads a little heavier than designed. Fine to ship, better to narrow down.
        /// </summary>
        Both = 0,
        /// <summary>One draw call through eyeeffectCanvas. The cheapest, and the exact look.</summary>
        Canvas = 1,
        /// <summary>OnGUI only. Needs no canvas, so it survives any canvas problem.</summary>
        Immediate = 2,
    }

    [Header("Drawing")]
    [Tooltip("Both draws through the canvas AND through OnGUI, so the eye appears even if the " +
             "canvas is misconfigured - at the cost of compositing the overlay twice, which " +
             "makes it slightly darker than intended. Once you know which one works, pick it.")]
    [SerializeField] private DrawPath drawPath = DrawPath.Both;
    [Tooltip("Vertical resolution of the eye buffer. It is stretched over the screen and the " +
             "softness comes from the upscale, so 144 already looks smooth at 4K.")]
    [SerializeField, Range(48, 480)] private int bufferHeight = 144;

    [Header("Post-processing (URP) - this is what blurs the world")]
    [Tooltip("Off makes this a lid-and-vignette effect only. The blur, the greyout, the swim and " +
             "the light sensitivity all live here.")]
    [SerializeField] private bool usePostProcessing = true;
    [Tooltip("The camera must have Post Processing ticked or none of this is visible. On by " +
             "default: tick it on the camera for you, and say so, rather than silently doing nothing.")]
    [SerializeField] private bool enableCameraPostIfOff = true;
    [Tooltip("How far out of focus the world goes. 1 is URP's maximum gaussian radius.")]
    [SerializeField, Range(0f, 1f)] private float blurStrength = 1f;
    [Tooltip("Colour drains out of the picture before you black out - the medical name for it is " +
             "literally a greyout. 0 keeps the world in full colour.")]
    [SerializeField, Range(0f, 1f)] private float greyoutStrength = 1f;
    [Tooltip("Chromatic aberration. Reads as the edges of things splitting - the closest thing " +
             "to double vision you can get cheaply.")]
    [SerializeField, Range(0f, 1f)] private float aberrationStrength = 0.8f;
    [Tooltip("Lens distortion. The room breathing and swimming around you.")]
    [SerializeField, Range(0f, 1f)] private float swimStrength = 0.8f;
    [Tooltip("Film grain. Starved-of-oxygen visual noise.")]
    [SerializeField, Range(0f, 1f)] private float grainStrength = 0.6f;

    [Header("Canvas")]
    [Tooltip("The canvas to draw into. Leave empty and it resolves itself: a Canvas on this " +
             "GameObject first, then one found by the name below, then a parent canvas.")]
    [SerializeField] private Canvas targetCanvas;
    [Tooltip("Name searched for when Target Canvas is empty and this object is not a canvas.")]
    [SerializeField] private string canvasName = "eyeeffectCanvas";
    [Tooltip("Last resort only. If no canvas can be found at all, build a private overlay canvas " +
             "so the effect still runs instead of silently doing nothing.")]
    [SerializeField] private bool buildCanvasIfMissing = true;
    [Tooltip("Sort order for that last-resort canvas. Never applied to a canvas you supplied - " +
             "set the Sort Order on eyeeffectCanvas yourself so it draws above your other UI.")]
    [SerializeField] private int fallbackSortingOrder = 30000;

    [Header("Timing")]
    [Tooltip("Default length of PlayFaint, in seconds.")]
    [SerializeField] private float faintDuration = 10f;
    [Tooltip("Default length of PlayWakeUp, in seconds.")]
    [SerializeField] private float wakeDuration = 10f;
    [Tooltip("On by default: a faint usually runs while the game is paused or slowed, and scaled " +
             "time would stall or stretch the animation.")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Look")]
    [SerializeField] private Color lidColor = Color.black;
    [Tooltip("Eyelids are translucent. With a bright room behind them a shut eye reads deep red, " +
             "not black - this is the colour it goes towards as the light comes up.")]
    [SerializeField] private Color lidGlowColor = new Color(0.30f, 0.045f, 0.03f, 1f);
    [Tooltip("Colour of the light that floods in through the parting lids on wake-up.")]
    [SerializeField] private Color lightColor = new Color(1f, 0.97f, 0.92f, 1f);
    [Tooltip("How much that light blows out the screen. 0 disables the photophobia wash.")]
    [SerializeField, Range(0f, 1f)] private float lightSensitivity = 0.45f;
    [Tooltip("How much the lid line bows. 0 is a flat shutter, 0.3 reads as an almond eye.")]
    [SerializeField, Range(0f, 0.6f)] private float lidArch = 0.3f;
    [Tooltip("Blur on the lid edge, as a fraction of half the screen height. A real lid sits " +
             "millimetres from the cornea, so it is never in focus.")]
    [SerializeField, Range(0.01f, 0.3f)] private float edgeSoftness = 0.09f;
    [Tooltip("The lids meet below the centre of the eye, not across it.")]
    [SerializeField, Range(-0.4f, 0.4f)] private float meetBias = -0.06f;
    [Tooltip("Exponent on the lower lid's travel. Below 1 makes it trail the upper lid, which is " +
             "what a real eye does - the upper lid covers roughly three quarters of the gap.")]
    [SerializeField, Range(0.05f, 1f)] private float lowerLidLag = 0.45f;

    [Header("Heartbeat")]
    [Tooltip("Pulse at the start of a faint. It slows as you go under.")]
    [SerializeField] private float faintBpmStart = 78f;
    [SerializeField] private float faintBpmEnd = 44f;
    [Tooltip("Pulse on waking. A headache throbs fast and hard, then eases.")]
    [SerializeField] private float wakeBpmStart = 96f;
    [SerializeField] private float wakeBpmEnd = 68f;

    [Header("Curves (normalised time -> how open the eye is)")]
    [Tooltip("Holds open while the world greys out, then two failing struggles and the collapse.")]
    [SerializeField] private AnimationCurve faintOpenness = BuildFaintCurve();
    [Tooltip("The cracks, the winces as light hits, and the settle.")]
    [SerializeField] private AnimationCurve wakeOpenness = BuildWakeCurve();

#if UNITY_EDITOR
    [Header("Demo (editor only - delete this block and Update when your triggers are in)")]
    [Tooltip("Draws a small status box over the game view so you can see what the effect is " +
             "doing without opening the Console.")]
    [SerializeField] private bool showStatus = true;
    [Tooltip("Tick while playing to run the faint. It unticks itself.")]
    [SerializeField] private bool playFaintNow;
    [Tooltip("Tick while playing to run the wake-up. It unticks itself.")]
    [SerializeField] private bool playWakeUpNow;
    [Tooltip("Tick to clear everything instantly - a faint ends holding black, so this is how " +
             "you get your view back without playing the wake-up.")]
    [SerializeField] private bool clearNow;
#endif

    /// <summary>Shared instance. Finds one in the scene, or builds a persistent one on demand.</summary>
    public static eye_effect Instance
    {
        get
        {
            if (instance != null) return instance;

            instance = FindAnyObjectByType<eye_effect>(FindObjectsInactive.Exclude);
            if (instance != null) return instance;

            GameObject host = new GameObject("eye_effect");
            // Marked before AddComponent, because AddComponent runs Awake straight away.
            if (Application.isPlaying) DontDestroyOnLoad(host);
            instance = host.AddComponent<eye_effect>();
            return instance;
        }
    }

    /// <summary>True while PlayFaint or PlayWakeUp is running.</summary>
    public bool IsPlaying => routine != null;

    /// <summary>1 when the eye is wide open, 0 when the lids are shut. Safe to read every frame.</summary>
    public float Openness => openness;

    /// <summary>1 - Openness. Handy for scaling muffled audio, camera roll or player control loss.</summary>
    public float Intensity => 1f - openness;

    /// <summary>0 sharp, 1 fully out of focus. Useful for fading voices in and out with the vision.</summary>
    public float Blur => blur;

    private static eye_effect instance;

    // The animated state. One set of numbers; Commit pushes them to both layers.
    private float openness = 1f;
    private float vignette;
    private float vignetteRadius = 1f;
    private float tilt;
    private Vector2 centreOffset;
    private float softness = 0.09f;
    private float flash;

    private float blur;
    private float desaturation;
    private float exposure;
    private float aberration;
    private float swim;
    private float grainAmount;
    private float glow;

    private Texture2D buffer;
    private Color32[] pixels;
    private RawImage image;
    private GameObject overlayRoot;
    private Coroutine routine;

    private Volume volume;
    private VolumeProfile profile;
    private DepthOfField dof;
    private ColorAdjustments grade;
    private ChromaticAberration chroma;
    private LensDistortion lens;
    private FilmGrain filmGrain;
    private Bloom bloom;
    private bool postUnavailable;

    // Set when the last Rebuild produced something worth drawing. Both renderers honour it, so an
    // idle eye costs one branch and nothing else.
    private bool visible;

#if UNITY_EDITOR
    private string statusLine = "eye_effect: starting";
#endif

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("eye_effect on '" + name + "': another instance is already active on '" +
                             instance.name + "'. The newest one now answers eye_effect.Instance.", this);
        }
        instance = this;

        Sanitise();
        EnsureOverlay();
        ApplyRestState(1f);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (buffer != null) Destroy(buffer);
        if (profile != null) Destroy(profile);
        if (volume != null) Destroy(volume.gameObject);
    }

    // Demo scaffolding only. The keys are written out literally rather than read from a serialized
    // KeyCode, because a serialized field is one more thing that can quietly be wrong and this
    // exists to prove the effect runs. Legacy Input is used deliberately, to match
    // intracting_drawer - Active Input Handling is "Both" in this project, so it is valid
    // alongside the Input System package. Delete this method once your real triggers are wired up.
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.I)) PlayFaint();
        if (Input.GetKeyDown(KeyCode.O)) PlayWakeUp();

#if UNITY_EDITOR
        if (playFaintNow)
        {
            playFaintNow = false;
            PlayFaint();
        }

        if (playWakeUpNow)
        {
            playWakeUpNow = false;
            PlayWakeUp();
        }

        if (clearNow)
        {
            clearNow = false;
            SetOpenInstant(1f);
        }
#endif
    }

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Passing out. The world softens and the colour drains out of it while your eyes are still
    /// open, the picture starts to swim, then tunnel vision closes in and the lids go - twice
    /// fighting their way back up before they drop for good. Ends holding full black; call
    /// PlayWakeUp or SetOpenInstant(1f) to clear it.
    /// </summary>
    public Coroutine PlayFaint(Action onComplete = null)
    {
        return PlayFaint(faintDuration, onComplete);
    }

    /// <inheritdoc cref="PlayFaint(Action)"/>
    public Coroutine PlayFaint(float duration, Action onComplete = null)
    {
        return Play(FaintRoutine(Mathf.Max(0.05f, duration), onComplete));
    }

    /// <summary>
    /// Coming round with a splitting head. Starts from black whatever the state was: the eye
    /// cracks open onto a blown-out blur, flinches shut against the light, then opens in stages
    /// while focus hunts for the world and the vignette throbs on every heartbeat. Ends clear.
    /// </summary>
    public Coroutine PlayWakeUp(Action onComplete = null)
    {
        return PlayWakeUp(wakeDuration, onComplete);
    }

    /// <inheritdoc cref="PlayWakeUp(Action)"/>
    public Coroutine PlayWakeUp(float duration, Action onComplete = null)
    {
        return Play(WakeRoutine(Mathf.Max(0.05f, duration), onComplete));
    }

    /// <summary>Cancels whatever is playing and leaves the eye exactly where it is.</summary>
    public void Stop()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    /// <summary>
    /// Snaps to a state with no animation. 0 holds full black (use it to cover a scene load
    /// between a faint and a wake-up), 1 clears everything.
    /// </summary>
    public void SetOpenInstant(float value)
    {
        Stop();
        ApplyRestState(value);
    }

    // ------------------------------------------------------------------ setup

    // Field initialisers only apply to fields Unity did not find in the scene file, so a component
    // that has been through a few script edits can be holding zeros the Inspector makes look
    // deliberate. A zero duration or a zero-alpha lid colour produces an effect that runs
    // perfectly and shows nothing, which is impossible to tell apart from a rendering bug. So
    // every value that can silently kill the effect is checked once, out loud.
    private void Sanitise()
    {
        if (lidColor.a < 0.01f)
        {
            Debug.LogWarning("eye_effect on '" + name + "': Lid Color alpha was " + lidColor.a +
                             ", which draws nothing. Forced to 1.", this);
            lidColor.a = 1f;
        }

        if (faintDuration < 0.1f) faintDuration = 10f;
        if (wakeDuration < 0.1f) wakeDuration = 10f;
        if (bufferHeight < 48) bufferHeight = 144;
        if (edgeSoftness < 0.005f) edgeSoftness = 0.09f;
        if (lowerLidLag < 0.05f) lowerLidLag = 0.45f;
        if (lightColor.a < 0.01f) lightColor.a = 1f;
        if (lidGlowColor.a < 0.01f) lidGlowColor = new Color(0.30f, 0.045f, 0.03f, 1f);
        if (faintBpmStart < 1f) faintBpmStart = 78f;
        if (faintBpmEnd < 1f) faintBpmEnd = 44f;
        if (wakeBpmStart < 1f) wakeBpmStart = 96f;
        if (wakeBpmEnd < 1f) wakeBpmEnd = 68f;
        if (string.IsNullOrEmpty(canvasName)) canvasName = "eyeeffectCanvas";

        if (faintOpenness == null || faintOpenness.length == 0) faintOpenness = BuildFaintCurve();
        if (wakeOpenness == null || wakeOpenness.length == 0) wakeOpenness = BuildWakeCurve();
    }

    // Rebuilt on demand rather than only in Awake: if this object survives a scene load
    // (DontDestroyOnLoad) while the canvas it drew into did not, the image is gone and the next
    // call has to find a canvas in the new scene.
    private bool EnsureOverlay()
    {
        if (image != null) return true;
        if (drawPath == DrawPath.Immediate) return true;   // OnGUI needs no canvas at all

        Canvas canvas = ResolveCanvas();
        if (canvas == null) return false;

        WarnIfCanvasCannotDraw(canvas);

        // Created WITH a RectTransform, and parented BEFORE the RawImage is added, so the Graphic
        // finds its canvas the first time it is enabled rather than caching a null one.
        overlayRoot = new GameObject("eye_overlay", typeof(RectTransform));
        overlayRoot.layer = canvas.gameObject.layer;
        overlayRoot.transform.SetParent(canvas.transform, false);

        RectTransform rt = (RectTransform)overlayRoot.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.localPosition = Vector3.zero;

        // No GraphicRaycaster and no EventSystem dependency: this overlay must never eat a click.
        image = overlayRoot.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.white;      // the tint lives in the texture, not here
        image.texture = buffer;

        overlayRoot.transform.SetAsLastSibling();   // draw over anything else on this canvas
        return true;
    }

    // Everything that makes a canvas silently draw nothing, reported up front rather than as an
    // invisible effect you have to guess at.
    private void WarnIfCanvasCannotDraw(Canvas canvas)
    {
        if (!canvas.isActiveAndEnabled)
        {
            Debug.LogWarning("eye_effect: canvas '" + canvas.name + "' is disabled, so the eye " +
                             "will not be visible until it is enabled.", canvas);
        }

        if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
        {
            Debug.LogError("eye_effect: canvas '" + canvas.name + "' is Screen Space - Camera " +
                           "with no Render Camera assigned, so it draws nothing. Set it to " +
                           "Screen Space - Overlay, or assign the camera.", canvas);
        }
    }

    private Canvas ResolveCanvas()
    {
        if (targetCanvas != null) return targetCanvas;

        // Dropped straight onto eyeeffectCanvas - the intended setup, and this works even if the
        // canvas is currently inactive.
        Canvas here = GetComponent<Canvas>();
        if (here != null) return here;

        if (!string.IsNullOrEmpty(canvasName))
        {
            GameObject named = GameObject.Find(canvasName);
            if (named != null)
            {
                Canvas found = named.GetComponent<Canvas>();
                if (found != null) return found;

                Debug.LogWarning("eye_effect on '" + name + "': found '" + canvasName +
                                 "' but it has no Canvas component.", this);
            }
        }

        Canvas parent = GetComponentInParent<Canvas>(true);
        if (parent != null) return parent;

        if (!buildCanvasIfMissing)
        {
            Debug.LogError("eye_effect on '" + name + "': no canvas named '" + canvasName +
                           "' and none assigned, so the canvas path cannot draw.", this);
            return null;
        }

        return BuildFallbackCanvas();
    }

    private Canvas BuildFallbackCanvas()
    {
        Debug.LogWarning("eye_effect on '" + name + "': no canvas named '" + canvasName +
                         "' was found, so a private overlay canvas was created. Assign " +
                         "Target Canvas or name your canvas '" + canvasName + "' to use yours.", this);

        GameObject go = new GameObject("eye_effect_canvas");
        go.transform.SetParent(transform, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = fallbackSortingOrder;

        return canvas;
    }

    // ------------------------------------------------------------------ post-processing

    // Builds a private global volume at a priority nothing else will beat. Every override starts
    // inactive, so until something plays this costs the renderer nothing at all.
    private bool EnsurePost()
    {
        if (!usePostProcessing || postUnavailable) return false;
        if (volume != null) return true;

        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
        {
            Debug.LogWarning("eye_effect on '" + name + "': the active render pipeline is not URP, " +
                             "so the blur and the greyout are unavailable. The lids and the tunnel " +
                             "vision still work.", this);
            postUnavailable = true;
            return false;
        }

        Camera cam = ResolveCamera();
        if (cam == null)
        {
            Debug.LogWarning("eye_effect on '" + name + "': no camera found, so the blur and the " +
                             "greyout are unavailable. Tag your camera MainCamera.", this);
            postUnavailable = true;
            return false;
        }

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data == null)
        {
            postUnavailable = true;
            return false;
        }

        if (!data.renderPostProcessing)
        {
            if (!enableCameraPostIfOff)
            {
                Debug.LogWarning("eye_effect on '" + name + "': camera '" + cam.name + "' has Post " +
                                 "Processing switched off, so the blur and the greyout cannot be " +
                                 "seen. Tick it on the camera, or tick Enable Camera Post If Off.", cam);
                postUnavailable = true;
                return false;
            }

            data.renderPostProcessing = true;
            Debug.LogWarning("eye_effect on '" + name + "': camera '" + cam.name + "' had Post " +
                             "Processing switched off. It has been switched on so the blur is " +
                             "visible, and left on.", cam);
        }

        // A volume is only seen by the camera if its LAYER is in the camera's Volume Mask, and
        // URP's default mask is the Default layer alone. Parenting this under eyeeffectCanvas
        // would put it on the UI layer and the camera would quietly ignore the whole thing, so it
        // is created at the scene root on a layer the camera actually looks at.
        GameObject go = new GameObject("eye_effect_post");
        go.layer = FirstLayerIn(data.volumeLayerMask);

        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10000f;
        volume.weight = 1f;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "eye_effect_profile";
        profile.hideFlags = HideFlags.HideAndDontSave;
        volume.profile = profile;

        // Gaussian rather than bokeh: it is far cheaper, and with the near plane as the start it
        // blurs the entire picture rather than just the background, which is what losing focus
        // actually looks like.
        dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(0f);
        dof.gaussianEnd.Override(30f);
        dof.gaussianMaxRadius.Override(0.5f);
        dof.highQualitySampling.Override(false);
        dof.active = false;

        grade = profile.Add<ColorAdjustments>(true);
        grade.postExposure.Override(0f);
        grade.saturation.Override(0f);
        grade.contrast.Override(0f);
        grade.active = false;

        chroma = profile.Add<ChromaticAberration>(true);
        chroma.intensity.Override(0f);
        chroma.active = false;

        lens = profile.Add<LensDistortion>(true);
        lens.intensity.Override(0f);
        lens.scale.Override(1f);
        lens.active = false;

        filmGrain = profile.Add<FilmGrain>(true);
        filmGrain.type.Override(FilmGrainLookup.Medium1);
        filmGrain.intensity.Override(0f);
        filmGrain.response.Override(0.8f);
        filmGrain.active = false;

        bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.scatter.Override(0.72f);
        bloom.intensity.Override(0f);
        bloom.active = false;

        volume.enabled = false;
        return true;
    }

    private void ApplyPost()
    {
        if (volume == null) return;

        float blurAmt = Mathf.Clamp01(blur) * Mathf.Clamp01(blurStrength);
        float greyAmt = Mathf.Clamp01(desaturation) * Mathf.Clamp01(greyoutStrength);
        float chromaAmt = Mathf.Clamp01(aberration) * Mathf.Clamp01(aberrationStrength);
        float swimAmt = Mathf.Clamp(swim, -1f, 1f) * Mathf.Clamp01(swimStrength);
        float grainAmt = Mathf.Clamp01(grainAmount) * Mathf.Clamp01(grainStrength);
        float glowAmt = Mathf.Max(0f, glow);

        bool any = blurAmt > 0.002f || greyAmt > 0.002f || Mathf.Abs(exposure) > 0.01f ||
                   chromaAmt > 0.002f || Mathf.Abs(swimAmt) > 0.002f || grainAmt > 0.002f ||
                   glowAmt > 0.002f;

        volume.enabled = any;
        if (!any) return;

        dof.active = blurAmt > 0.002f;
        if (dof.active)
        {
            // Pulling the end of the ramp in towards the camera is what makes the near world lose
            // focus too, so the two together read as your eyes giving up rather than a camera rack.
            dof.gaussianEnd.value = Mathf.Lerp(30f, 0.3f, blurAmt);
            dof.gaussianMaxRadius.value = Mathf.Lerp(0.5f, 1.5f, blurAmt);
        }

        grade.active = greyAmt > 0.002f || Mathf.Abs(exposure) > 0.01f;
        if (grade.active)
        {
            grade.saturation.value = -100f * greyAmt;
            grade.contrast.value = -30f * greyAmt;      // flat and washed out, not crushed
            grade.postExposure.value = exposure;
        }

        chroma.active = chromaAmt > 0.002f;
        if (chroma.active) chroma.intensity.value = chromaAmt;

        lens.active = Mathf.Abs(swimAmt) > 0.002f;
        if (lens.active)
        {
            lens.intensity.value = swimAmt;
            // Distorting without scaling up pulls the screen edges away and shows black corners.
            lens.scale.value = 1f + Mathf.Abs(swimAmt) * 0.35f;
        }

        filmGrain.active = grainAmt > 0.002f;
        if (filmGrain.active) filmGrain.intensity.value = grainAmt;

        bloom.active = glowAmt > 0.002f;
        if (bloom.active)
        {
            bloom.intensity.value = glowAmt;
            bloom.threshold.value = Mathf.Lerp(0.9f, 0.3f, Mathf.Clamp01(glowAmt));
        }
    }

    private Camera ResolveCamera()
    {
        Camera cam = Camera.main;
        if (cam != null) return cam;

        return FindAnyObjectByType<Camera>(FindObjectsInactive.Exclude);
    }

    private static int FirstLayerIn(LayerMask mask)
    {
        int bits = mask.value;
        if (bits == 0) return 0;

        for (int i = 0; i < 32; i++)
        {
            if ((bits & (1 << i)) != 0) return i;
        }

        return 0;
    }

    // ------------------------------------------------------------------ the eye

    // Rasterises the lids and the tunnel into the buffer. Everything is in -1..1 screen space, so
    // the maths is independent of the resolution and of the aspect ratio.
    private void Rebuild()
    {
        int h = Mathf.Clamp(bufferHeight, 48, 480);
        int w = Mathf.Max(16, Mathf.RoundToInt(h * 16f / 9f));

        if (buffer == null || buffer.height != h || buffer.width != w)
        {
            if (buffer != null) Destroy(buffer);

            buffer = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "eye_effect_buffer",
                // Bilinear plus clamp is what turns a 256x144 buffer into a soft 4K image. Clamp
                // matters: repeat would wrap the lid round to the opposite edge of the screen.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            pixels = new Color32[w * h];

            if (image != null) image.texture = buffer;
        }

        float open = Mathf.Clamp01(openness);
        float soft = Mathf.Max(0.004f, softness);
        float arch = Mathf.Clamp(lidArch, 0f, 0.9f);
        float weakest = Mathf.Max(0.05f, 1f - arch);
        float skewMax = Mathf.Abs(tilt) * 0.35f;

        // The arch pulls the ends of the lid back towards the meeting line, so a lid that is
        // "fully open" has to sit further off screen than the screen edge or a sliver shows in
        // the corners. Solve for the position that clears it at the weakest point.
        float meet = Mathf.Clamp(meetBias, -0.4f, 0.4f);
        float upperClear = meet + (1f + soft + skewMax - meet) / weakest;
        float lowerClear = meet - (meet - (-1f - soft - skewMax)) / weakest;

        float lowerOpen = Mathf.Pow(open, Mathf.Max(0.05f, lowerLidLag));

        // Tunnel vision. The aperture is wider than it is tall, like a real palpebral fissure.
        float aperture = Mathf.Max(0.02f, Mathf.Clamp01(vignetteRadius));
        float apertureX = aperture * 1.45f;
        float vigStrength = Mathf.Clamp01(vignette);

        float flashAmount = Mathf.Clamp01(flash);

        // Light coming through the lid tissue, rather than a black shutter in front of it.
        float glowMix = Mathf.Clamp01(flashAmount * 1.8f);
        float lidR = Mathf.Lerp(lidColor.r, lidGlowColor.r, glowMix);
        float lidG = Mathf.Lerp(lidColor.g, lidGlowColor.g, glowMix);
        float lidB = Mathf.Lerp(lidColor.b, lidGlowColor.b, glowMix);
        float lidA = lidColor.a;

        float flashR = lightColor.r, flashG = lightColor.g, flashB = lightColor.b;

        bool anything = false;

        for (int py = 0; py < h; py++)
        {
            float v = (py + 0.5f) / h * 2f - 1f;
            int row = py * w;

            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w * 2f - 1f;

                // --- lids ------------------------------------------------------------------
                float archMul = 1f - arch * u * u;
                float skew = tilt * u * 0.35f;

                float upperEdge = meet + skew + (upperClear - meet) * open * archMul;
                float lowerEdge = meet + skew + (lowerClear - meet) * lowerOpen * archMul;

                float aUpper = SmoothStep01((v - (upperEdge - soft)) / soft);
                float aLower = SmoothStep01(((lowerEdge + soft) - v) / soft);
                float a = aUpper > aLower ? aUpper : aLower;

                // --- tunnel vision ---------------------------------------------------------
                if (vigStrength > 0.001f)
                {
                    float dx = (u - centreOffset.x) / apertureX;
                    float dy = (v - centreOffset.y) / aperture;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float vig = SmoothStep01((d - 1f) / 0.9f) * vigStrength;
                    if (vig > a) a = vig;
                }

                a *= lidA;

                float r = lidR, g = lidG, b = lidB;

                // --- light flooding in through the lids ------------------------------------
                if (flashAmount > 0.002f)
                {
                    float outA = a + flashAmount * (1f - a);
                    if (outA > 0.0001f)
                    {
                        float inv = 1f / outA;
                        float fw = flashAmount * (1f - a);
                        r = (lidR * a + flashR * fw) * inv;
                        g = (lidG * a + flashG * fw) * inv;
                        b = (lidB * a + flashB * fw) * inv;
                    }
                    a = outA;
                }

                if (a > 0.004f) anything = true;

                int i = row + px;
                pixels[i].r = (byte)(Mathf.Clamp01(r) * 255f);
                pixels[i].g = (byte)(Mathf.Clamp01(g) * 255f);
                pixels[i].b = (byte)(Mathf.Clamp01(b) * 255f);
                pixels[i].a = (byte)(Mathf.Clamp01(a) * 255f);
            }
        }

        buffer.SetPixels32(pixels);
        buffer.Apply(false);

        visible = anything;
        if (image != null)
        {
            if (image.texture != buffer) image.texture = buffer;
            bool wantCanvas = visible && drawPath != DrawPath.Immediate;
            if (image.enabled != wantCanvas) image.enabled = wantCanvas;
        }
    }

    // OnGUI is the renderer that cannot fail: no canvas, no material, no sorting, no render
    // pipeline involvement. It is here so the eye is visible even when the canvas path is not
    // working, and so a build behaves the same as the editor.
    private void OnGUI()
    {
        if (drawPath == DrawPath.Canvas) return;
        if (Event.current.type != EventType.Repaint) return;

        if (visible && buffer != null)
        {
            GUI.depth = -1000;
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), buffer,
                            ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

#if UNITY_EDITOR
        if (showStatus)
        {
            GUI.depth = -1001;
            GUI.Label(new Rect(10f, 10f, 1200f, 22f), statusLine);
        }
#endif
    }

    // ------------------------------------------------------------------ driving

    private Coroutine Play(IEnumerator body)
    {
        Stop();

        if (!isActiveAndEnabled)
        {
            Debug.LogError("eye_effect on '" + name + "': the component is disabled, so the effect " +
                           "cannot run. Enable the GameObject before calling.", this);
            return null;
        }

        EnsureOverlay();
        EnsurePost();

        routine = StartCoroutine(body);
        return routine;
    }

    private IEnumerator FaintRoutine(float duration, Action onComplete)
    {
        float t = 0f;
        float beatPhase = 0f;

        while (t < 1f)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            dt = Mathf.Min(dt, 0.1f);          // one long frame must not skip half the faint
            t = Mathf.Min(1f, t + dt / duration);

            // The pulse slows and weakens as blood pressure drops.
            beatPhase += dt * Mathf.Lerp(faintBpmStart, faintBpmEnd, t) / 60f;
            float beat = Heartbeat(beatPhase) * 0.35f * (1f - t * 0.5f);

            // The greyout. This leads everything else: the world goes soft and colourless while
            // the eyes are still open, which is the order a real faint happens in. By the time the
            // lids matter you can barely see anyway.
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.72f, t));

            blur = Mathf.Clamp01(fade + beat * 0.25f);
            desaturation = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.70f, t));
            aberration = Mathf.Clamp01(fade * 0.7f + beat * 0.25f);
            grainAmount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.95f, t)) * 0.6f;

            // Lights smear and bloom as the eye stops resolving them, then the dark takes it.
            glow = fade * 0.6f * (1f - t * 0.7f);

            // A small lift first - the pounding before you go - then the lights go out.
            exposure = Mathf.Lerp(0.2f, -2.4f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.95f, t)))
                       + beat * 0.5f * (1f - t);

            float open = Evaluate(faintOpenness, t, 1f - t);

            // Tunnel vision, accelerating: a grey-out collapses at the end rather than fading
            // evenly. The corners of the screen go first.
            float tunnel = Mathf.Pow(t, 1.9f);

            openness = Mathf.Clamp01(open - beat * 0.05f);
            vignette = Mathf.Clamp01(tunnel * 1.35f + beat * 0.28f + fade * 0.25f);
            vignetteRadius = Mathf.Clamp01(1f - tunnel * 0.98f - beat * 0.06f);

            // The head lolls and sinks once the neck gives up. Two detuned sines so the sway
            // never repeats on an obvious beat.
            float sway = Mathf.Sin(t * 3.1f) * 0.5f + Mathf.Sin(t * 1.3f + 1.1f) * 0.5f;
            tilt = sway * 0.26f * t;
            centreOffset = new Vector2(sway * 0.06f * t, -0.14f * t * t);
            swim = Mathf.Clamp(sway * 0.28f * t + beat * 0.12f, -1f, 1f);

            softness = edgeSoftness * (1f + t * 1.8f);   // focus goes with consciousness
            flash = 0f;

            Commit();
            yield return null;
        }

        // Land on an exact, fully black frame rather than wherever the last tick happened to be,
        // and drop the post-processing: the screen is solid black now, so blurring it is work
        // nobody can see.
        openness = 0f;
        vignette = 1f;
        vignetteRadius = 0.02f;
        flash = 0f;
        ClearPostState();
        Commit();

        routine = null;
        onComplete?.Invoke();
    }

    private IEnumerator WakeRoutine(float duration, Action onComplete)
    {
        // Waking always begins in the dark, whatever the eye was doing before.
        openness = 0f;
        vignette = 1f;
        vignetteRadius = 0.25f;
        blur = 1f;
        desaturation = 1f;

        float t = 0f;
        float beatPhase = 0f;

        while (t < 1f)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            dt = Mathf.Min(dt, 0.1f);
            t = Mathf.Min(1f, t + dt / duration);

            beatPhase += dt * Mathf.Lerp(wakeBpmStart, wakeBpmEnd, t) / 60f;
            float beatAmp = Mathf.Pow(1f - t, 0.8f);         // the throb fades as the head clears
            float beat = Heartbeat(beatPhase) * beatAmp;

            float open = Evaluate(wakeOpenness, t, t);

            // Every pulse squeezes the eye a little and presses the dark in from the edges.
            openness = Mathf.Clamp01(open - beat * 0.10f);

            // Focus does not come back in a straight line. The eye hunts for it, overshoots, and
            // settles - and it cannot focus at all through a half-closed lid, so a wince blurs
            // the world again on its way down.
            float recover = Mathf.Pow(1f - t, 1.25f);
            float hunt = Mathf.Sin(t * 16f) * 0.12f * Mathf.Pow(1f - t, 1.6f);
            blur = Mathf.Clamp01(recover + hunt + (1f - openness) * 0.45f * Mathf.Pow(1f - t, 0.8f));

            desaturation = Mathf.Clamp01(Mathf.Pow(1f - t, 1.3f) * 0.9f);
            aberration = Mathf.Clamp01(Mathf.Pow(1f - t, 1.15f) * 0.75f + beat * 0.2f);
            grainAmount = Mathf.Pow(1f - t, 1.5f) * 0.5f;

            // Photophobia. Light only hurts once the lid is actually letting it in, so the sting
            // lands on the openings and eases off every time the eye flinches shut.
            float exposed = Mathf.Clamp01(openness * 2.2f) * Mathf.Pow(1f - t, 1.8f);
            exposure = exposed * 2.6f * (0.6f + beat * 0.4f);
            glow = exposed * 1.5f;
            flash = exposed * lightSensitivity * (0.55f + beat * 0.45f);

            float pressure = Mathf.Pow(1f - t, 1.4f);
            vignette = Mathf.Clamp01(pressure * 0.9f + beat * 0.5f);
            vignetteRadius = Mathf.Clamp01(1f - pressure * 0.55f - beat * 0.18f);

            // The room swims until it settles.
            float roll = Mathf.Sin(t * 7.3f) * (1f - t);
            tilt = roll * 0.14f;
            centreOffset = new Vector2(roll * 0.04f, Mathf.Sin(t * 5.1f + 0.7f) * 0.03f * (1f - t));
            swim = Mathf.Clamp(roll * 0.3f + beat * 0.12f, -1f, 1f);

            softness = edgeSoftness * (1f + (1f - t) * 2.4f);

            Commit();
            yield return null;
        }

        // Land on an exact clear frame. Deliberately not SetOpenInstant: that calls Stop(), and a
        // coroutine must not stop itself.
        ApplyRestState(1f);

        routine = null;
        onComplete?.Invoke();
    }

    // A still state: no blur, no tunnel, no tilt - just how far open the eye is.
    private void ApplyRestState(float value)
    {
        openness = Mathf.Clamp01(value);
        vignette = 0f;
        vignetteRadius = 1f;
        tilt = 0f;
        centreOffset = Vector2.zero;
        softness = edgeSoftness;
        flash = 0f;

        ClearPostState();
        Commit();
    }

    private void ClearPostState()
    {
        blur = 0f;
        desaturation = 0f;
        exposure = 0f;
        aberration = 0f;
        swim = 0f;
        grainAmount = 0f;
        glow = 0f;
    }

    // Pushes this frame's values into the eye buffer and the volume.
    private void Commit()
    {
        Rebuild();
        ApplyPost();

#if UNITY_EDITOR
        if (showStatus)
        {
            statusLine = "eye_effect  open " + openness.ToString("F2") +
                         "  blur " + blur.ToString("F2") +
                         "  grey " + desaturation.ToString("F2") +
                         "  exp " + exposure.ToString("F2") +
                         "  vig " + vignette.ToString("F2") +
                         "  rad " + vignetteRadius.ToString("F2") +
                         "  | visible " + visible +
                         "  post " + (volume != null ? (volume.enabled ? "on" : "idle")
                                                     : (postUnavailable ? "UNAVAILABLE" : "not built")) +
                         "  canvasImage " + (image != null ? (image.enabled ? "on" : "off") : "NONE") +
                         "  path " + drawPath;
        }
#endif
    }

    private static float SmoothStep01(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t * t * (3f - 2f * t);
    }

    private static float Evaluate(AnimationCurve curve, float t, float fallback)
    {
        if (curve == null || curve.length == 0) return Mathf.Clamp01(fallback);
        return Mathf.Clamp01(curve.Evaluate(t));
    }

    // Two offset gaussians: the sharp "lub" and the softer "dub" a fifth of a beat later.
    private static float Heartbeat(float phase)
    {
        float p = phase - Mathf.Floor(phase);

        float a = (p - 0.08f) / 0.055f;
        float b = (p - 0.26f) / 0.075f;

        return Mathf.Clamp01(Mathf.Exp(-a * a) + Mathf.Exp(-b * b) * 0.55f);
    }

    // Keyframes default to flat tangents, which eases in and out between every pair - the right
    // feel for eyelids, and it makes close-together keys read as a snap.
    //
    // The lids deliberately hold open through the first third. The world is already blurring and
    // greying by then, and a faint where the eyes close first just reads as a wipe.
    private static AnimationCurve BuildFaintCurve()
    {
        return new AnimationCurve(
            new Keyframe(0.00f, 1.00f),
            new Keyframe(0.24f, 0.96f),   // heaviness, barely
            new Keyframe(0.42f, 0.84f),
            new Keyframe(0.54f, 0.60f),
            new Keyframe(0.60f, 0.72f),   // fights it
            new Keyframe(0.72f, 0.34f),
            new Keyframe(0.78f, 0.46f),   // fights it again, weaker
            new Keyframe(0.88f, 0.12f),
            new Keyframe(0.93f, 0.19f),   // last flicker
            new Keyframe(1.00f, 0.00f));
    }

    private static AnimationCurve BuildWakeCurve()
    {
        return new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.07f, 0.00f),
            new Keyframe(0.12f, 0.16f),   // cracks open
            new Keyframe(0.18f, 0.04f),   // light hurts, shuts again
            new Keyframe(0.27f, 0.10f),
            new Keyframe(0.36f, 0.34f),
            new Keyframe(0.42f, 0.20f),   // wince
            new Keyframe(0.54f, 0.52f),
            new Keyframe(0.60f, 0.38f),   // wince, weaker
            new Keyframe(0.74f, 0.76f),
            new Keyframe(0.82f, 0.65f),
            new Keyframe(0.93f, 0.96f),
            new Keyframe(1.00f, 1.00f));
    }
}

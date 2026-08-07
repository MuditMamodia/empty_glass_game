using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Screen blackout assembled from sprite "patches" that bloom in front of the camera one by one
/// until nothing is left but black - the way vision goes when a character is passing out.
///
/// The patches are not decoration on top of a fade: they ARE the fade for most of the run. A plain
/// alpha fade reads as a camera cut, while patchwork occlusion reads as *your eyes* failing,
/// because that is how real vision loss arrives - in blotches, out of order, from the edges in.
/// A solid black image only seals the last of the gaps at the very end, so the screen is
/// guaranteed to finish 100% opaque no matter how the random layout landed.
///
/// Setup: drop this on an empty GameObject, drag the sub-sprites of your patch sheet into
/// Patch Sprites, and call Play(). The Canvas is built for you at runtime if you do not assign one.
/// </summary>
[AddComponentMenu("There Is Something/Eye Patch Blackout")]
[DisallowMultipleComponent]
public class EyePatchBlackout : MonoBehaviour
{
    [Header("Sprites")]
    [Tooltip("The sub-sprites sliced out of your patch sheet. Drag all of them in - one is picked " +
             "at random per patch. Leave empty and plain squares are used instead.")]
    [SerializeField] private Sprite[] patchSprites;

    [Tooltip("Tint applied to every patch. Sprites are multiplied by this, so a white/grey sheet " +
             "goes black here without you having to re-author the art.")]
    [SerializeField] private Color patchColor = Color.black;

    [Header("Timing")]
    [Tooltip("Total length of the effect, start to fully black.")]
    [SerializeField, Min(0.05f)] private float duration = 5f;

    [Tooltip("Fraction of the duration the patches spawn over. The remainder is the final seal.")]
    [SerializeField, Range(0.1f, 1f)] private float patchPhase = 0.8f;

    [Tooltip("How long a single patch takes to bloom from nothing to full size.")]
    [SerializeField, Min(0.01f)] private float patchGrowTime = 0.45f;

    [Tooltip("When the solid black seal starts fading in, as a fraction of the duration.")]
    [SerializeField, Range(0f, 1f)] private float sealStart = 0.7f;

    [Tooltip("Run on unscaled time so the blackout still plays while the game is paused " +
             "(Time.timeScale = 0), which is usually what you want for a death or cutscene.")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Coverage")]
    [Tooltip("Patches are placed on a jittered grid so the screen is covered evenly rather than " +
             "clumping in one corner. More cells = finer, busier patchwork.")]
    [SerializeField, Min(1)] private int columns = 6;
    [SerializeField, Min(1)] private int rows = 4;
    [SerializeField, Min(1)] private int patchesPerCell = 2;

    [Tooltip("Patch size as a multiple of the grid cell. Above 1 the patches overlap, which is " +
             "what keeps holes from showing between them.")]
    [SerializeField, Min(0.1f)] private float patchScale = 2.1f;

    [Tooltip("How far a patch may wander from its cell centre, as a fraction of the cell.")]
    [SerializeField, Range(0f, 1f)] private float positionJitter = 0.5f;

    [Tooltip("Random spin per patch. Square patches rotate without opening gaps, so leave this on " +
             "unless your sheet has a readable up direction.")]
    [SerializeField] private bool randomRotation = true;

    [Header("Motion")]
    [Tooltip("How far each patch drifts outward from screen centre as it blooms. This is what " +
             "sells 'coming towards the eyes' instead of just appearing flat on the glass.")]
    [SerializeField, Range(0f, 1f)] private float approachFromCentre = 0.35f;

    [Tooltip("Scale a patch starts at before it blooms.")]
    [SerializeField, Range(0f, 1f)] private float startScale = 0.15f;

    [Tooltip("Shape of a single patch's bloom. Ease-out feels like a blink; linear feels mechanical.")]
    [SerializeField] private AnimationCurve growEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Maps reveal order to reveal time. Ease-in-out holds a few patches back so the last " +
             "of the vision goes all at once rather than at a steady drip.")]
    [SerializeField] private AnimationCurve revealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Canvas")]
    [Tooltip("Leave empty and a screen-space overlay Canvas is created at runtime.")]
    [SerializeField] private Canvas targetCanvas;

    [Tooltip("Sorting order for the generated Canvas. High so nothing draws over the blackout.")]
    [SerializeField] private int sortingOrder = 5000;

    [Tooltip("Swallow clicks once the effect starts, so the player cannot press UI they can no " +
             "longer see.")]
    [SerializeField] private bool blockInput = true;

    [Header("Playback")]
    [SerializeField] private bool playOnStart;

    [Tooltip("Re-roll the layout every Play(). Turn off and the same seed reproduces the exact " +
             "same patchwork every time - useful for a scripted moment you want to look authored.")]
    [SerializeField] private bool randomiseSeed = true;
    [SerializeField] private int seed = 12345;

    [Space]
    public UnityEvent onBlackoutComplete;

    private readonly List<Patch> patches = new List<Patch>();
    private RectTransform patchRoot;
    private Image sealImage;
    private Coroutine running;
    private bool built;

    /// <summary>True while the blackout is animating.</summary>
    public bool IsPlaying => running != null;

    private class Patch
    {
        public RectTransform Rect;
        public Image Image;
        public Vector2 From;
        public Vector2 To;
        public float Rotation;
        public float StartTime;
    }

    private void Awake()
    {
        // Built up front rather than on Play(), because instantiating several dozen UI objects is
        // exactly the kind of hitch you do not want on the frame the scare lands.
        EnsureBuilt();
        Clear();
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    // ---------------------------------------------------------------- public API

    /// <summary>Runs the blackout from clear to fully black over <c>duration</c> seconds.</summary>
    [ContextMenu("Play")]
    public void Play()
    {
        if (!isActiveAndEnabled) return;

        EnsureBuilt();
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Run());
    }

    /// <summary>Wipes the screen back to clear immediately and stops any run in progress.</summary>
    [ContextMenu("Clear")]
    public void Clear()
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }

        for (int i = 0; i < patches.Count; i++)
            patches[i].Rect.gameObject.SetActive(false);

        if (sealImage != null)
        {
            SetAlpha(sealImage, 0f);
            sealImage.raycastTarget = false;
        }
    }

    /// <summary>Snaps straight to full black without animating - for a hard cut.</summary>
    public void BlackoutInstant()
    {
        EnsureBuilt();
        Clear();
        SetAlpha(sealImage, 1f);
        sealImage.raycastTarget = blockInput;
    }

    /// <summary>
    /// Throws away the pooled patches so the next Play() rebuilds them. Call after changing the
    /// grid settings from script; the inspector fields are read fresh on every Play() anyway.
    /// </summary>
    public void Rebuild()
    {
        for (int i = 0; i < patches.Count; i++)
            if (patches[i].Rect != null) Destroy(patches[i].Rect.gameObject);

        patches.Clear();
        built = false;
        EnsureBuilt();
    }

    // ---------------------------------------------------------------- run

    private IEnumerator Run()
    {
        LayoutPatches();

        if (blockInput) sealImage.raycastTarget = true;

        float sealBegin = duration * sealStart;
        float sealSpan = Mathf.Max(0.0001f, duration - sealBegin);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Min(elapsed, duration);

            for (int i = 0; i < patches.Count; i++)
                ApplyPatch(patches[i], t);

            SetAlpha(sealImage, Mathf.Clamp01((t - sealBegin) / sealSpan));
            yield return null;
        }

        // Land on the exact end state rather than wherever the last delta happened to stop.
        for (int i = 0; i < patches.Count; i++)
            ApplyPatch(patches[i], duration);

        SetAlpha(sealImage, 1f);

        running = null;
        onBlackoutComplete?.Invoke();
    }

    private void ApplyPatch(Patch patch, float time)
    {
        float local = (time - patch.StartTime) / patchGrowTime;

        if (local <= 0f)
        {
            if (patch.Rect.gameObject.activeSelf) patch.Rect.gameObject.SetActive(false);
            return;
        }

        if (!patch.Rect.gameObject.activeSelf) patch.Rect.gameObject.SetActive(true);

        float e = growEase.Evaluate(Mathf.Clamp01(local));
        patch.Rect.anchoredPosition = Vector2.LerpUnclamped(patch.From, patch.To, e);
        patch.Rect.localScale = Vector3.one * Mathf.LerpUnclamped(startScale, 1f, e);
        patch.Rect.localRotation = Quaternion.Euler(0f, 0f, patch.Rotation);
        SetAlpha(patch.Image, Mathf.Clamp01(e));
    }

    // ---------------------------------------------------------------- layout

    private void LayoutPatches()
    {
        int wanted = columns * rows * patchesPerCell;
        if (patches.Count != wanted) Rebuild();

        // A Canvas built this frame has not been laid out yet, so its rect still reads zero and
        // every patch would come out sized to nothing. Force the layout before measuring.
        Canvas.ForceUpdateCanvases();

        Rect area = patchRoot.rect;
        if (area.width < 1f || area.height < 1f)
            area = new Rect(-Screen.width * 0.5f, -Screen.height * 0.5f, Screen.width, Screen.height);

        float cellW = area.width / columns;
        float cellH = area.height / rows;
        float size = Mathf.Max(cellW, cellH) * patchScale;

        var rng = new System.Random(randomiseSeed ? Random.Range(int.MinValue, int.MaxValue) : seed);

        // Reveal order is the shuffle, not the placement: every cell still gets its patch, but the
        // order they arrive in is scrambled so the blackout spreads unpredictably instead of
        // wiping left-to-right like a loading bar.
        var order = new int[wanted];
        for (int i = 0; i < wanted; i++) order[i] = i;
        for (int i = wanted - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // All patches must have finished blooming by the time the patch phase ends, so the window
        // they *start* in is shortened by one bloom.
        float window = Mathf.Max(0f, duration * patchPhase - patchGrowTime);

        for (int i = 0; i < wanted; i++)
        {
            Patch patch = patches[i];
            int cell = i / patchesPerCell;
            int col = cell % columns;
            int row = cell / columns;

            float cx = area.xMin + (col + 0.5f) * cellW;
            float cy = area.yMin + (row + 0.5f) * cellH;
            cx += ((float)rng.NextDouble() - 0.5f) * cellW * positionJitter;
            cy += ((float)rng.NextDouble() - 0.5f) * cellH * positionJitter;

            patch.To = new Vector2(cx, cy);
            patch.From = Vector2.Lerp(patch.To, Vector2.zero, approachFromCentre);
            patch.Rotation = randomRotation ? (float)rng.NextDouble() * 360f : 0f;

            patch.Rect.sizeDelta = new Vector2(size, size);
            patch.Image.sprite = PickSprite(rng);
            patch.Image.color = new Color(patchColor.r, patchColor.g, patchColor.b, 0f);
            patch.Rect.gameObject.SetActive(false);

            float fraction = wanted > 1 ? order[i] / (float)(wanted - 1) : 0f;
            patch.StartTime = window * Mathf.Clamp01(revealCurve.Evaluate(fraction));
        }
    }

    private Sprite PickSprite(System.Random rng)
    {
        if (patchSprites == null || patchSprites.Length == 0) return null;

        // Tolerate empty slots in the array - it is easy to leave a hole after dragging sprites in.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Sprite candidate = patchSprites[rng.Next(patchSprites.Length)];
            if (candidate != null) return candidate;
        }

        for (int i = 0; i < patchSprites.Length; i++)
            if (patchSprites[i] != null) return patchSprites[i];

        return null;
    }

    // ---------------------------------------------------------------- build

    private void EnsureBuilt()
    {
        if (built && patchRoot != null) return;

        if (targetCanvas == null) targetCanvas = BuildCanvas();

        if (patchRoot == null)
        {
            patchRoot = CreateStretchedChild("Patches", targetCanvas.transform);
        }

        if (sealImage == null)
        {
            RectTransform sealRect = CreateStretchedChild("Seal", targetCanvas.transform);
            sealRect.SetAsLastSibling();                 // always on top of the patches
            sealImage = sealRect.gameObject.AddComponent<Image>();
            sealImage.color = new Color(patchColor.r, patchColor.g, patchColor.b, 0f);
            sealImage.raycastTarget = false;
        }

        int wanted = columns * rows * patchesPerCell;
        while (patches.Count < wanted) patches.Add(CreatePatch(patches.Count));

        built = true;
    }

    private Canvas BuildCanvas()
    {
        var go = new GameObject("EyeBlackout Canvas", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // Match on the larger axis so patches sized off the reference still overhang the real
        // screen on ultrawide displays rather than leaving a bare strip.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        if (blockInput) go.AddComponent<GraphicRaycaster>();

        return canvas;
    }

    private Patch CreatePatch(int index)
    {
        var go = new GameObject($"Patch_{index:00}", typeof(RectTransform), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(patchRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = new Color(patchColor.r, patchColor.g, patchColor.b, 0f);

        go.SetActive(false);

        return new Patch { Rect = rect, Image = image };
    }

    private static RectTransform CreateStretchedChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null) return;
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keeps the seal from starting before the patches are done arriving, which would fade the
        // whole thing to black early and waste the patchwork.
        sealStart = Mathf.Clamp(sealStart, 0f, 0.99f);
        patchGrowTime = Mathf.Min(patchGrowTime, duration * patchPhase);
    }
#endif
}

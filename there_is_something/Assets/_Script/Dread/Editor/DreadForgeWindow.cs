using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The front end for the Dread toolkit. Three stages, meant to be run in order on one object:
/// break it, work out how it has aged, then bake that into a texture you can ship.
/// </summary>
public class DreadForgeWindow : EditorWindow
{
    private enum Stage { Break, Decay, Retexture }

    [SerializeField] private Stage stage = Stage.Break;
    [SerializeField] private GameObject target;
    [SerializeField] private string outputFolder = "Assets/Dread_Generated";
    [SerializeField] private Material interiorMaterial;

    [SerializeField] private DreadFracture.Settings fracture = new DreadFracture.Settings();
    [SerializeField] private DreadSurface.Settings surface = new DreadSurface.Settings();
    [SerializeField] private DreadTextureBaker.Settings texture = new DreadTextureBaker.Settings();

    [SerializeField] private Vector3 planePoint;
    [SerializeField] private Quaternion planeRotation = Quaternion.identity;
    [SerializeField] private bool planeInitialised;

    private Vector2 scroll;
    private string status = "";
    private MessageType statusType = MessageType.None;

    [MenuItem("Tools/Dread Forge")]
    public static void Open()
    {
        var window = GetWindow<DreadForgeWindow>("Dread Forge");
        window.minSize = new Vector2(330f, 460f);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGui;
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGui;
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null && selected.GetComponent<MeshFilter>() != null && selected != target)
        {
            target = selected;
            planeInitialised = false;
            Repaint();
        }
    }

    // ------------------------------------------------------------------ window

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        var so = new SerializedObject(this);

        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        target = (GameObject)EditorGUILayout.ObjectField("Object", target, typeof(GameObject), true);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        Mesh mesh = CurrentMesh();
        if (target == null)
        {
            EditorGUILayout.HelpBox("Select a GameObject with a MeshFilter.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        if (mesh == null)
        {
            EditorGUILayout.HelpBox("That object has no mesh.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        if (!mesh.isReadable)
        {
            EditorGUILayout.HelpBox(
                "'" + mesh.name + "' is not readable. Select the source model in the Project " +
                "window and tick Read/Write Enabled, then Apply.", MessageType.Error);
            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.LabelField("Mesh", mesh.name + "   " + mesh.vertexCount.ToString("N0") +
                                           " verts, " + mesh.subMeshCount + " submesh(es)");

        EditorGUILayout.Space();
        stage = (Stage)GUILayout.Toolbar((int)stage, new[] { "1. Break", "2. Decay", "3. Retexture" });
        EditorGUILayout.Space();

        switch (stage)
        {
            case Stage.Break: DrawBreak(so, mesh); break;
            case Stage.Decay: DrawDecay(so, mesh); break;
            case Stage.Retexture: DrawRetexture(so, mesh); break;
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(status, statusType);
        }

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ stage 1: break

    private void DrawBreak(SerializedObject so, Mesh mesh)
    {
        EnsurePlane(mesh);

        EditorGUILayout.HelpBox(
            "Drag the red plane in the Scene view to place the break. Switch to the Rotate tool " +
            "to angle it - a break that is not axis-aligned reads as far more violent.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Align X")) { planeRotation = Quaternion.LookRotation(Vector3.up, Vector3.right); }
            if (GUILayout.Button("Align Y")) { planeRotation = Quaternion.identity; }
            if (GUILayout.Button("Align Z")) { planeRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward); }
            if (GUILayout.Button("Recentre")) { planeInitialised = false; EnsurePlane(mesh); }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Snapped")) ApplyBreakPreset(0.012f, 9f, 2, 0.02f, 0f, 0f);
            if (GUILayout.Button("Torn Fabric")) ApplyBreakPreset(0.05f, 4f, 4, 0.07f, 0.05f, 0.02f);
            if (GUILayout.Button("Splintered")) ApplyBreakPreset(0.035f, 12f, 4, 0.09f, 0.02f, 0.04f);
            if (GUILayout.Button("Wrenched")) ApplyBreakPreset(0.08f, 3f, 3, 0.12f, 0.12f, 0.06f);
        }

        EditorGUILayout.Space();
        DrawSettings(so, "fracture");

        EditorGUILayout.Space();
        interiorMaterial = (Material)EditorGUILayout.ObjectField(
            new GUIContent("Interior Material", "Applied to the exposed inside of the break. " +
                                                "Leave empty and one is generated for you."),
            interiorMaterial, typeof(Material), false);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(target == null))
        {
            if (GUILayout.Button("Break It", GUILayout.Height(30f))) DoBreak(mesh);
        }
    }

    private void ApplyBreakPreset(float amplitude, float frequency, int octaves,
                                  float capDepth, float sag, float splay)
    {
        fracture.tearAmplitude = amplitude;
        fracture.tearFrequency = frequency;
        fracture.tearOctaves = octaves;
        fracture.capDepth = capDepth;
        fracture.sagAmount = sag;
        fracture.splayAmount = splay;
        GUI.FocusControl(null);
    }

    private void DoBreak(Mesh mesh)
    {
        Transform t = target.transform;

        fracture.planePoint = t.InverseTransformPoint(planePoint);
        fracture.planeNormal = t.InverseTransformDirection(planeRotation * Vector3.up);

        DreadFracture.Result result;
        try
        {
            EditorUtility.DisplayProgressBar("Dread Forge", "Tearing geometry", 0.4f);
            result = DreadFracture.Split(mesh, fracture);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!result.ok) { SetStatus(result.error, MessageType.Error); return; }

        string folder = EnsureFolder();
        string baseName = SanitiseName(target.name);

        AssetDatabase.CreateAsset(result.above, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + "_A.asset"));
        AssetDatabase.CreateAsset(result.below, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + "_B.asset"));

        Material[] pieceMaterials = BuildPieceMaterials(folder);

        var root = new GameObject(target.name + "_broken");
        Undo.RegisterCreatedObjectUndo(root, "Dread Break");
        root.transform.SetParent(t.parent, false);
        root.transform.localPosition = t.localPosition;
        root.transform.localRotation = t.localRotation;
        root.transform.localScale = t.localScale;

        CreatePiece(root.transform, "Piece_A", result.above, pieceMaterials);
        CreatePiece(root.transform, "Piece_B", result.below, pieceMaterials);

        // Keep the original rather than deleting it - you will want to re-break with different
        // settings, and an unrecoverable operation is a bad default.
        Undo.RecordObject(target, "Dread Break");
        target.SetActive(false);

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;

        SetStatus("Broken into 2 pieces (" + result.above.vertexCount.ToString("N0") + " + " +
                  result.below.vertexCount.ToString("N0") + " verts). The original was disabled, " +
                  "not deleted. Next: select a piece and run stage 2.", MessageType.Info);
    }

    private void CreatePiece(Transform parent, string name, Mesh mesh, Material[] materials)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Dread Break");
        go.transform.SetParent(parent, false);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
    }

    private Material[] BuildPieceMaterials(string folder)
    {
        var renderer = target.GetComponent<Renderer>();
        Material[] source = renderer != null ? renderer.sharedMaterials : new Material[0];

        var result = new Material[source.Length + 1];
        for (int i = 0; i < source.Length; i++) result[i] = source[i];

        result[source.Length] = interiorMaterial != null
            ? interiorMaterial
            : CreateDefaultInterior(folder);

        return result;
    }

    private Material CreateDefaultInterior(string folder)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = "Dread_Interior" };

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", texture.woundColor);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", texture.woundColor);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.08f);

        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Dread_Interior.mat");
        AssetDatabase.CreateAsset(mat, path);

        interiorMaterial = mat;
        return mat;
    }

    // ------------------------------------------------------------------ stage 2: decay

    private void DrawDecay(SerializedObject so, Mesh mesh)
    {
        EditorGUILayout.HelpBox(
            "Reads the geometry and works out where this object would be dirty, worn, damp and " +
            "rotten. Writes the answer into vertex colours; nothing visible changes yet.",
            MessageType.None);

        EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Neglected")) ApplyDecayPreset(0.5f, 0.4f, 0.3f, 0.5f, 0.25f);
            if (GUILayout.Button("Damp Cellar")) ApplyDecayPreset(0.7f, 0.5f, 0.35f, 0.85f, 0.7f);
            if (GUILayout.Button("Long Abandoned")) ApplyDecayPreset(0.85f, 0.7f, 0.7f, 0.6f, 0.4f);
            if (GUILayout.Button("Infested")) ApplyDecayPreset(0.9f, 0.55f, 0.4f, 1f, 0.9f);
        }

        EditorGUILayout.Space();
        DrawSettings(so, "surface");

        EditorGUILayout.Space();
        if (GUILayout.Button("Bake Decay", GUILayout.Height(30f))) DoDecay(mesh);
    }

    private void ApplyDecayPreset(float occlusionGrime, float cavity, float upFacing,
                                  float damp, float streak)
    {
        surface.grimeFromOcclusion = occlusionGrime;
        surface.grimeFromCavity = cavity;
        surface.grimeFromUpFacing = upFacing;
        surface.dampFromOcclusion = damp;
        surface.dampStreaking = streak;
        GUI.FocusControl(null);
    }

    private void DoDecay(Mesh mesh)
    {
        string folder = EnsureFolder();
        string existingPath = AssetDatabase.GetAssetPath(mesh);

        // Bake in place if this mesh is already one of ours; otherwise copy, so we never write
        // into an imported model asset.
        bool inPlace = !string.IsNullOrEmpty(existingPath) &&
                       existingPath.Replace('\\', '/').StartsWith(folder + "/");

        Mesh workingMesh;
        if (inPlace)
        {
            workingMesh = mesh;
        }
        else
        {
            workingMesh = Object.Instantiate(mesh);
            workingMesh.name = mesh.name + "_decayed";
        }

        bool ok;
        try
        {
            ok = DreadSurface.Bake(workingMesh, surface,
                (label, f) => EditorUtility.DisplayCancelableProgressBar("Dread Forge", label, f));
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!ok)
        {
            if (!inPlace) Object.DestroyImmediate(workingMesh);
            SetStatus("Decay bake cancelled or failed.", MessageType.Warning);
            return;
        }

        if (!inPlace)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/" + SanitiseName(workingMesh.name) + ".asset");
            AssetDatabase.CreateAsset(workingMesh, path);

            Undo.RecordObject(target.GetComponent<MeshFilter>(), "Dread Decay");
            target.GetComponent<MeshFilter>().sharedMesh = workingMesh;
        }
        else
        {
            EditorUtility.SetDirty(workingMesh);
        }

        AssetDatabase.SaveAssets();
        SetStatus("Decay baked into vertex colours. Nothing looks different yet - run stage 3 " +
                  "to turn it into a texture.", MessageType.Info);
    }

    // ------------------------------------------------------------------ stage 3: retexture

    private void DrawRetexture(SerializedObject so, Mesh mesh)
    {
        EditorGUILayout.HelpBox(
            "Composites the decay onto a copy of the existing albedo and writes a PNG. The " +
            "result is an ordinary texture - it needs no custom shader, and your artist can " +
            "open it and paint over the top.", MessageType.None);

        if (mesh.colors == null || mesh.colors.Length != mesh.vertexCount)
        {
            EditorGUILayout.HelpBox("This mesh has no vertex colours yet. Run stage 2 first.",
                                    MessageType.Warning);
        }

        if (DreadTextureBaker.HasTilingUvs(mesh, out Rect uvBounds))
        {
            EditorGUILayout.HelpBox(
                "UVs run outside 0..1 (" + uvBounds.ToString() + "), which means this material " +
                "tiles. Many points on the surface share one texel, so a baked texture cannot " +
                "hold per-location damage - the result will smear. This mesh needs a second, " +
                "non-overlapping UV set before stage 3 is meaningful.", MessageType.Warning);
        }

        EditorGUILayout.Space();
        DrawSettings(so, "texture");

        EditorGUILayout.Space();
        if (GUILayout.Button("Bake Texture", GUILayout.Height(30f))) DoRetexture(mesh);
    }

    private void DoRetexture(Mesh mesh)
    {
        var renderer = target.GetComponent<Renderer>();
        if (renderer == null) { SetStatus("No Renderer on the target.", MessageType.Error); return; }

        Material[] materials = renderer.sharedMaterials;
        var sources = new Texture2D[Mathf.Max(1, materials.Length)];
        var tints = new Color[Mathf.Max(1, materials.Length)];

        for (int i = 0; i < sources.Length; i++)
        {
            Material m = i < materials.Length ? materials[i] : null;
            sources[i] = GetAlbedo(m);
            tints[i] = GetTint(m);
        }

        Texture2D albedo, masks;
        bool ok;

        try
        {
            EditorUtility.DisplayProgressBar("Dread Forge", "Rasterising UV space", 0.5f);
            ok = DreadTextureBaker.Bake(mesh, sources, tints, texture, out albedo, out masks);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!ok)
        {
            SetStatus("Texture bake failed. The mesh needs UVs and vertex colours.", MessageType.Error);
            return;
        }

        string folder = EnsureFolder();
        string baseName = SanitiseName(target.name);

        string albedoPath = DreadTextureBaker.WritePng(albedo, folder, baseName + "_albedo", linear: false);
        Object.DestroyImmediate(albedo);

        if (masks != null)
        {
            DreadTextureBaker.WritePng(masks, folder, baseName + "_masks", linear: true);
            Object.DestroyImmediate(masks);
        }

        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);

        Material source = materials.Length > 0 && materials[0] != null
            ? materials[0]
            : new Material(Shader.Find("Universal Render Pipeline/Lit"));

        var baked = new Material(source) { name = baseName + "_dread" };
        SetAlbedo(baked, imported);
        // The tint was composited into the pixels, so leaving it on the material would apply
        // it a second time.
        if (baked.HasProperty("_BaseColor")) baked.SetColor("_BaseColor", Color.white);
        else if (baked.HasProperty("_Color")) baked.SetColor("_Color", Color.white);

        string materialPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + "_dread.mat");
        AssetDatabase.CreateAsset(baked, materialPath);

        Undo.RecordObject(renderer, "Dread Retexture");
        var assigned = new Material[materials.Length == 0 ? 1 : materials.Length];
        for (int i = 0; i < assigned.Length; i++) assigned[i] = baked;
        renderer.sharedMaterials = assigned;

        AssetDatabase.SaveAssets();
        SetStatus("Wrote " + albedoPath + " and applied a new material.", MessageType.Info);
    }

    private static Texture2D GetAlbedo(Material m)
    {
        if (m == null) return null;
        if (m.HasProperty("_BaseMap")) return m.GetTexture("_BaseMap") as Texture2D;
        if (m.HasProperty("_MainTex")) return m.GetTexture("_MainTex") as Texture2D;
        return m.mainTexture as Texture2D;
    }

    private static void SetAlbedo(Material m, Texture2D t)
    {
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
    }

    private static Color GetTint(Material m)
    {
        if (m == null) return Color.white;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color")) return m.GetColor("_Color");
        return Color.white;
    }

    // ------------------------------------------------------------------ scene view

    private void OnSceneGui(SceneView view)
    {
        if (target == null || stage != Stage.Break) return;

        Mesh mesh = CurrentMesh();
        if (mesh == null) return;

        EnsurePlane(mesh);

        EditorGUI.BeginChangeCheck();

        if (Tools.current == Tool.Rotate)
        {
            planeRotation = Handles.RotationHandle(planeRotation, planePoint);
        }
        else
        {
            planePoint = Handles.PositionHandle(planePoint, planeRotation);
        }

        if (EditorGUI.EndChangeCheck()) Repaint();

        // Draw the cut as a quad sized to the object, so you can see exactly what it will hit.
        Bounds b = target.GetComponent<Renderer>() != null
            ? target.GetComponent<Renderer>().bounds
            : new Bounds(target.transform.position, Vector3.one);

        float extent = Mathf.Max(b.extents.magnitude, 0.1f);
        Vector3 right = planeRotation * Vector3.right * extent;
        Vector3 forward = planeRotation * Vector3.forward * extent;

        var corners = new[]
        {
            planePoint - right - forward,
            planePoint + right - forward,
            planePoint + right + forward,
            planePoint - right + forward,
        };

        Handles.DrawSolidRectangleWithOutline(corners,
            new Color(0.85f, 0.1f, 0.1f, 0.12f),
            new Color(1f, 0.25f, 0.2f, 0.9f));

        Handles.color = new Color(1f, 0.35f, 0.25f, 0.9f);
        Handles.DrawLine(planePoint, planePoint + planeRotation * Vector3.up * extent * 0.35f);
    }

    private void EnsurePlane(Mesh mesh)
    {
        if (planeInitialised || target == null) return;

        var renderer = target.GetComponent<Renderer>();
        planePoint = renderer != null ? renderer.bounds.center : target.transform.position;
        planeRotation = Quaternion.identity;
        planeInitialised = true;
    }

    // ------------------------------------------------------------------ plumbing

    private Mesh CurrentMesh()
    {
        if (target == null) return null;
        var filter = target.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private void DrawSettings(SerializedObject so, string propertyName)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null) return;

        so.Update();

        // Draw the children directly rather than the parent, so there is no redundant foldout
        // wrapping the whole block.
        SerializedProperty iterator = property.Copy();
        SerializedProperty end = iterator.GetEndProperty();

        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
        {
            enterChildren = false;
            EditorGUILayout.PropertyField(iterator, true);
        }

        so.ApplyModifiedProperties();
    }

    private string EnsureFolder()
    {
        string folder = string.IsNullOrEmpty(outputFolder) ? "Assets/Dread_Generated" : outputFolder.TrimEnd('/');

        if (!AssetDatabase.IsValidFolder(folder))
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        return folder;
    }

    private static string SanitiseName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private void SetStatus(string message, MessageType type)
    {
        status = message;
        statusType = type;
        if (type == MessageType.Error) Debug.LogError("Dread Forge: " + message);
    }
}

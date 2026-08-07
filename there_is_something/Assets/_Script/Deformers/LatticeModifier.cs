using UnityEngine;

/// <summary>
/// Deforms this object's mesh through a <see cref="LatticeDeformer"/> cage.
///
/// The source mesh asset is never touched. On enable this component takes a private copy,
/// deforms the copy, and puts the original back when it is disabled or destroyed - so removing
/// the component always leaves the object exactly as it was found.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
[AddComponentMenu("Deformers/Lattice Modifier")]
public class LatticeModifier : MonoBehaviour
{
    public enum UpdateMode
    {
        /// <summary>Re-deform only when the cage or the relative transform actually changed.</summary>
        WhenDirty = 0,

        /// <summary>Re-deform every frame. Only needed if something animates the cage from code.</summary>
        EveryFrame = 1,

        /// <summary>Never re-deform on its own; call <see cref="Deform"/> yourself.</summary>
        Manual = 2,
    }

    [Header("References")]
    [Tooltip("The cage that drives this mesh. Any LatticeDeformer in the scene will do; it " +
             "does not have to be a parent.")]
    [SerializeField] private LatticeDeformer lattice;

    [Header("Update")]
    [SerializeField] private UpdateMode updateMode = UpdateMode.WhenDirty;

    [Header("Mesh")]
    [Tooltip("Needed for lighting to follow the deformation. Costs roughly as much as the " +
             "deform itself, and it rebuilds smoothing from scratch - so a mesh with authored " +
             "hard edges will come back fully smoothed. Turn off for subtle deformations.")]
    [SerializeField] private bool recalculateNormals = true;

    [Tooltip("Only needed if the material uses a normal map.")]
    [SerializeField] private bool recalculateTangents = false;

    // Serialized so the original survives play mode transitions, domain reloads and crashes.
    // Without this, a reload that leaves our generated mesh assigned would make the next bind
    // treat an already-deformed mesh as the source and bake the deformation in permanently.
    [SerializeField, HideInInspector] private Mesh sourceMesh;

    [System.NonSerialized] private MeshFilter meshFilter;
    [System.NonSerialized] private Mesh workingMesh;
    [System.NonSerialized] private Vector3[] sourceVertices;
    [System.NonSerialized] private Vector3[] deformedVertices;
    [System.NonSerialized] private bool bound;

    [System.NonSerialized] private int lastVersion = -1;
    [System.NonSerialized] private Matrix4x4 lastRelative;

    public LatticeDeformer Lattice
    {
        get => lattice;
        set { lattice = value; lastVersion = -1; }
    }

    public int VertexCount => sourceVertices != null ? sourceVertices.Length : 0;
    public bool IsBound => bound;

    private void OnEnable()
    {
        Bind();
    }

    private void OnDisable()
    {
        Release();
    }

    private void OnDestroy()
    {
        Release();
    }

    private void OnValidate()
    {
        // Force the next tick to re-deform, whatever the update mode thinks.
        lastVersion = -1;
    }

    private void LateUpdate()
    {
        if (updateMode == UpdateMode.Manual) return;
        if (!bound || lattice == null) return;
        if (updateMode == UpdateMode.WhenDirty && !IsDirty()) return;

        Deform();
    }

    // ------------------------------------------------------------------ binding

    /// <summary>
    /// Grabs the source mesh and creates the private copy we deform into. Safe to call more
    /// than once.
    /// </summary>
    public void Bind()
    {
        Release();

        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) return;

        if (GetComponent<SkinnedMeshRenderer>() != null)
        {
            Debug.LogWarning("LatticeModifier on '" + name + "': this object has a " +
                             "SkinnedMeshRenderer. Lattice deformation runs on the bind-pose " +
                             "mesh and would be overwritten by skinning every frame, so only " +
                             "MeshFilter meshes are supported.", this);
        }

        // Prefer the remembered original. meshFilter.sharedMesh may currently be a leftover
        // generated mesh from before a domain reload, or a destroyed one.
        if (sourceMesh == null) sourceMesh = meshFilter.sharedMesh;
        if (sourceMesh == null)
        {
            Debug.LogError("LatticeModifier on '" + name + "' has no mesh to deform.", this);
            return;
        }

        sourceVertices = sourceMesh.vertices;
        if (sourceVertices == null || sourceVertices.Length == 0)
        {
            Debug.LogError("LatticeModifier on '" + name + "': source mesh '" + sourceMesh.name +
                           "' has no vertices. If it is an imported asset, enable Read/Write " +
                           "in its import settings.", this);
            sourceVertices = null;
            return;
        }

        deformedVertices = new Vector3[sourceVertices.Length];

        workingMesh = Instantiate(sourceMesh);
        workingMesh.name = sourceMesh.name + " (Lattice)";
        // Not an asset and must never end up serialized into the scene file.
        workingMesh.hideFlags = HideFlags.HideAndDontSave;
        workingMesh.MarkDynamic();

        meshFilter.sharedMesh = workingMesh;

        bound = true;
        lastVersion = -1;

        if (lattice != null) Deform();
    }

    /// <summary>Puts the original mesh back and throws away the copy.</summary>
    public void Release()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();

        if (meshFilter != null && sourceMesh != null && meshFilter.sharedMesh == workingMesh)
        {
            meshFilter.sharedMesh = sourceMesh;
        }

        if (workingMesh != null)
        {
            if (Application.isPlaying) Destroy(workingMesh);
            else DestroyImmediate(workingMesh);
        }

        workingMesh = null;
        sourceVertices = null;
        deformedVertices = null;
        bound = false;
        lastVersion = -1;
    }

    // ------------------------------------------------------------------ deformation

    /// <summary>Runs every vertex through the cage and pushes the result to the mesh copy.</summary>
    public void Deform()
    {
        if (!bound || lattice == null || workingMesh == null) return;

        // Mesh local -> lattice local. Recomputed every deform because either object may have
        // moved: sliding a mesh through a stationary cage is a legitimate way to animate this.
        Matrix4x4 meshToLattice = lattice.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        Matrix4x4 latticeToMesh = meshToLattice.inverse;

        for (int i = 0; i < sourceVertices.Length; i++)
        {
            Vector3 inCage = meshToLattice.MultiplyPoint3x4(sourceVertices[i]);
            Vector3 displaced = inCage + lattice.SampleOffset(lattice.NormalizedFromLocal(inCage));
            deformedVertices[i] = latticeToMesh.MultiplyPoint3x4(displaced);
        }

        workingMesh.SetVertices(deformedVertices);

        if (recalculateNormals) workingMesh.RecalculateNormals();
        if (recalculateTangents) workingMesh.RecalculateTangents();

        // Always. Stale bounds get the renderer culled while it is still on screen.
        workingMesh.RecalculateBounds();

        lastVersion = lattice.Version;
        lastRelative = meshToLattice;
    }

    private bool IsDirty()
    {
        if (lattice.Version != lastVersion) return true;

        Matrix4x4 relative = lattice.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        return relative != lastRelative;
    }
}

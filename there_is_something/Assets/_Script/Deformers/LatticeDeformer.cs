using UnityEngine;

/// <summary>
/// How the control point field is interpolated between points.
/// </summary>
public enum LatticeInterpolation
{
    /// <summary>Trilinear. Cheapest. Creases visibly along cell boundaries.</summary>
    Linear = 0,

    /// <summary>Cubic, and passes exactly through every control point. Best when you want a
    /// point to land where you dragged it.</summary>
    CatmullRom = 1,

    /// <summary>Cubic and approximating - the surface is pulled toward the points but does not
    /// pass through them. Smoothest result, which is why Blender defaults to it.</summary>
    BSpline = 2,
}

/// <summary>
/// A 3D cage of control points that deforms meshes passing through it, the same idea as
/// Blender's Lattice object. This component owns nothing but the cage; put a
/// <see cref="LatticeModifier"/> on each mesh you want it to affect.
///
/// Keeping the cage on its own object is what makes it useful: one lattice can drive several
/// meshes, and moving the lattice through a mesh changes the deformation. If you want it to
/// behave like a self-contained modifier instead, just parent the lattice to the mesh.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Deformers/Lattice Deformer")]
public class LatticeDeformer : MonoBehaviour
{
    public const int MinResolution = 2;
    public const int MaxResolution = 32;

    private const float MinSize = 1e-4f;

    [Header("Cage")]
    [Tooltip("Control points per axis. 2 means corners only; raise an axis to get finer " +
             "control along it. Changing this resamples the existing shape rather than " +
             "discarding your edits.")]
    [SerializeField] private Vector3Int resolution = new Vector3Int(3, 3, 3);

    [Tooltip("Size of the cage box, in this object's local space.")]
    [SerializeField] private Vector3 size = Vector3.one;

    [Tooltip("Centre of the cage box, in this object's local space.")]
    [SerializeField] private Vector3 center = Vector3.zero;

    [Header("Deformation")]
    [SerializeField] private LatticeInterpolation interpolation = LatticeInterpolation.BSpline;

    [Tooltip("Scales the whole deformation. 0 is the undeformed mesh, 1 is the cage as drawn.")]
    [SerializeField, Range(0f, 1f)] private float strength = 1f;

    [Tooltip("On: vertices outside the cage follow the nearest face of it, so the mesh never " +
             "tears at the boundary. Off: they are left completely alone, which is what you " +
             "want when the cage is only meant to touch part of a large mesh - but it will " +
             "crease where the cage ends.")]
    [SerializeField] private bool affectOutsideBounds = true;

    // Offset of each control point from its rest position, in this object's local space.
    // Flattened as index = x + resolution.x * (y + resolution.y * z).
    // Stored as offsets rather than absolute positions so that resizing or re-centring the
    // cage moves the shape with it instead of tearing it apart.
    [SerializeField, HideInInspector] private Vector3[] offsets = new Vector3[0];

    // The resolution `offsets` was actually built for. Diverges from `resolution` the moment
    // someone edits the field in the inspector, which is how we detect that we must resample.
    [SerializeField, HideInInspector] private Vector3Int bakedResolution;

    // Bumped on every change. LatticeModifier polls this rather than relying on an event, so
    // it stays correct across domain reloads and undo.
    [System.NonSerialized] private int version;

    // Scratch for one sample. Cubic needs 4 taps per axis, linear 2.
    [System.NonSerialized] private readonly int[] idxX = new int[4];
    [System.NonSerialized] private readonly int[] idxY = new int[4];
    [System.NonSerialized] private readonly int[] idxZ = new int[4];
    [System.NonSerialized] private readonly float[] wX = new float[4];
    [System.NonSerialized] private readonly float[] wY = new float[4];
    [System.NonSerialized] private readonly float[] wZ = new float[4];

    public Vector3Int Resolution => resolution;
    public Vector3 Size => size;
    public Vector3 Center => center;
    public LatticeInterpolation Interpolation => interpolation;
    public float Strength => strength;
    public bool AffectOutsideBounds => affectOutsideBounds;
    public int PointCount => resolution.x * resolution.y * resolution.z;

    /// <summary>Incremented whenever anything that changes the deformation changes.</summary>
    public int Version => version;

    private void Awake()
    {
        EnsureOffsets();
    }

    private void OnValidate()
    {
        resolution = ClampResolution(resolution);

        // A zero or negative extent would divide by zero in NormalizedFromLocal.
        size.x = Mathf.Max(size.x, MinSize);
        size.y = Mathf.Max(size.y, MinSize);
        size.z = Mathf.Max(size.z, MinSize);

        EnsureOffsets();
        version++;
    }

    // ------------------------------------------------------------------ point access

    public int GetIndex(int x, int y, int z)
    {
        return x + resolution.x * (y + resolution.y * z);
    }

    /// <summary>Where control point (x,y,z) sits with no deformation, in local space.</summary>
    public Vector3 GetRestPosition(int x, int y, int z)
    {
        return new Vector3(
            center.x + (Fraction(x, resolution.x) - 0.5f) * size.x,
            center.y + (Fraction(y, resolution.y) - 0.5f) * size.y,
            center.z + (Fraction(z, resolution.z) - 0.5f) * size.z);
    }

    public Vector3 GetOffset(int x, int y, int z)
    {
        EnsureOffsets();
        return offsets[GetIndex(x, y, z)];
    }

    public void SetOffset(int x, int y, int z, Vector3 offset)
    {
        EnsureOffsets();
        offsets[GetIndex(x, y, z)] = offset;
        version++;
    }

    /// <summary>Current (deformed) position of a control point, in local space.</summary>
    public Vector3 GetPosition(int x, int y, int z)
    {
        return GetRestPosition(x, y, z) + GetOffset(x, y, z);
    }

    public void SetPosition(int x, int y, int z, Vector3 localPosition)
    {
        SetOffset(x, y, z, localPosition - GetRestPosition(x, y, z));
    }

    /// <summary>Flattens the cage back to its rest shape without touching resolution or size.</summary>
    public void ResetPoints()
    {
        EnsureOffsets();
        System.Array.Clear(offsets, 0, offsets.Length);
        version++;
    }

    /// <summary>
    /// Changes resolution. When <paramref name="resample"/> is true the existing shape is
    /// re-sampled into the new grid, so raising resolution to get finer control keeps what you
    /// already sculpted instead of throwing it away.
    /// </summary>
    public void SetResolution(Vector3Int newResolution, bool resample = true)
    {
        newResolution = ClampResolution(newResolution);
        if (newResolution == bakedResolution && offsets != null && offsets.Length == Count(newResolution))
        {
            resolution = newResolution;
            return;
        }

        Rebuild(newResolution, resample);
        version++;
    }

    /// <summary>
    /// Sizes and centres the cage so it encloses <paramref name="worldBounds"/>, with an
    /// optional padding in world units. Resolution and the sculpted shape are preserved.
    /// </summary>
    public void FitTo(Bounds worldBounds, float padding = 0f)
    {
        // The bounds are an AABB in world space; this transform may be rotated relative to it,
        // so take the eight corners into local space and re-fit there.
        Vector3 e = worldBounds.extents;
        Vector3 c = worldBounds.center;

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);

            Vector3 local = transform.InverseTransformPoint(corner);
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }

        center = (min + max) * 0.5f;
        size = max - min + Vector3.one * (padding * 2f);

        size.x = Mathf.Max(size.x, MinSize);
        size.y = Mathf.Max(size.y, MinSize);
        size.z = Mathf.Max(size.z, MinSize);

        version++;
    }

    // ------------------------------------------------------------------ sampling

    /// <summary>Maps a local-space point into cage space, where (0,0,0)..(1,1,1) is the box.</summary>
    public Vector3 NormalizedFromLocal(Vector3 localPoint)
    {
        return new Vector3(
            (localPoint.x - center.x) / size.x + 0.5f,
            (localPoint.y - center.y) / size.y + 0.5f,
            (localPoint.z - center.z) / size.z + 0.5f);
    }

    /// <summary>
    /// The displacement the cage applies at a normalized position. This is the whole modifier
    /// in one function; everything else is bookkeeping.
    /// </summary>
    public Vector3 SampleOffset(Vector3 normalized)
    {
        EnsureOffsets();

        if (!affectOutsideBounds &&
            (normalized.x < 0f || normalized.x > 1f ||
             normalized.y < 0f || normalized.y > 1f ||
             normalized.z < 0f || normalized.z > 1f))
        {
            return Vector3.zero;
        }

        return Sample(offsets, resolution, normalized, interpolation) * strength;
    }

    /// <summary>Runs a local-space point through the cage and returns the deformed local point.</summary>
    public Vector3 DeformLocalPoint(Vector3 localPoint)
    {
        return localPoint + SampleOffset(NormalizedFromLocal(localPoint));
    }

    private Vector3 Sample(Vector3[] field, Vector3Int res, Vector3 normalized, LatticeInterpolation mode)
    {
        if (field == null || field.Length != Count(res)) return Vector3.zero;

        // Clamping here is what makes vertices outside the cage follow its nearest face
        // instead of flying off along an extrapolated curve.
        int nx = ComputeWeights(Mathf.Clamp01(normalized.x) * (res.x - 1), res.x, mode, idxX, wX);
        int ny = ComputeWeights(Mathf.Clamp01(normalized.y) * (res.y - 1), res.y, mode, idxY, wY);
        int nz = ComputeWeights(Mathf.Clamp01(normalized.z) * (res.z - 1), res.z, mode, idxZ, wZ);

        Vector3 sum = Vector3.zero;

        for (int k = 0; k < nz; k++)
        {
            float weightZ = wZ[k];
            if (weightZ == 0f) continue;

            int planeBase = res.y * idxZ[k];

            for (int j = 0; j < ny; j++)
            {
                float weightYZ = weightZ * wY[j];
                if (weightYZ == 0f) continue;

                int rowBase = res.x * (idxY[j] + planeBase);

                for (int i = 0; i < nx; i++)
                {
                    float weight = weightYZ * wX[i];
                    if (weight == 0f) continue;

                    sum += field[rowBase + idxX[i]] * weight;
                }
            }
        }

        return sum;
    }

    /// <summary>
    /// Fills the taps and weights for one axis and returns how many taps were used.
    /// <paramref name="coord"/> is in cell space: 0 .. res-1.
    /// </summary>
    private static int ComputeWeights(float coord, int res, LatticeInterpolation mode,
                                      int[] indices, float[] weights)
    {
        // res is >= 2 everywhere (ClampResolution guarantees it), so res - 2 >= 0 and the
        // cell index below always has a valid right-hand neighbour.
        int cell = Mathf.Clamp(Mathf.FloorToInt(coord), 0, res - 2);
        float t = Mathf.Clamp01(coord - cell);

        if (mode == LatticeInterpolation.Linear)
        {
            indices[0] = cell;
            indices[1] = cell + 1;
            weights[0] = 1f - t;
            weights[1] = t;
            return 2;
        }

        float t2 = t * t;
        float t3 = t2 * t;
        float w0, w1, w2, w3;

        if (mode == LatticeInterpolation.CatmullRom)
        {
            w0 = -0.5f * t3 + t2 - 0.5f * t;
            w1 = 1.5f * t3 - 2.5f * t2 + 1f;
            w2 = -1.5f * t3 + 2f * t2 + 0.5f * t;
            w3 = 0.5f * t3 - 0.5f * t2;
        }
        else // BSpline
        {
            const float Sixth = 1f / 6f;
            float inv = 1f - t;
            w0 = inv * inv * inv * Sixth;
            w1 = (3f * t3 - 6f * t2 + 4f) * Sixth;
            w2 = (-3f * t3 + 3f * t2 + 3f * t + 1f) * Sixth;
            w3 = t3 * Sixth;
        }

        // Clamping the outer taps repeats the edge point, which keeps the field well defined at
        // the cage boundary and lets a cubic mode work even at resolution 2.
        indices[0] = Mathf.Clamp(cell - 1, 0, res - 1);
        indices[1] = cell;
        indices[2] = cell + 1;
        indices[3] = Mathf.Clamp(cell + 2, 0, res - 1);

        weights[0] = w0;
        weights[1] = w1;
        weights[2] = w2;
        weights[3] = w3;
        return 4;
    }

    // ------------------------------------------------------------------ storage

    private void EnsureOffsets()
    {
        resolution = ClampResolution(resolution);

        if (offsets != null && offsets.Length == Count(resolution) && bakedResolution == resolution)
        {
            return;
        }

        Rebuild(resolution, true);
    }

    /// <summary>
    /// Reallocates the offset array at a new resolution, optionally sampling the old field into
    /// it first. Must never call EnsureOffsets, or it recurses.
    /// </summary>
    private void Rebuild(Vector3Int newResolution, bool resample)
    {
        Vector3[] previous = offsets;
        Vector3Int previousResolution = bakedResolution;

        Vector3[] rebuilt = new Vector3[Count(newResolution)];

        bool canResample = resample
                           && previous != null
                           && previous.Length > 0
                           && previous.Length == Count(previousResolution);

        if (canResample)
        {
            // Resample through the same interpolation the cage uses, so raising resolution
            // reproduces the shape you already had rather than a faceted approximation of it.
            for (int z = 0; z < newResolution.z; z++)
            {
                for (int y = 0; y < newResolution.y; y++)
                {
                    for (int x = 0; x < newResolution.x; x++)
                    {
                        Vector3 normalized = new Vector3(
                            Fraction(x, newResolution.x),
                            Fraction(y, newResolution.y),
                            Fraction(z, newResolution.z));

                        int index = x + newResolution.x * (y + newResolution.y * z);
                        rebuilt[index] = Sample(previous, previousResolution, normalized, interpolation);
                    }
                }
            }
        }

        offsets = rebuilt;
        resolution = newResolution;
        bakedResolution = newResolution;
    }

    /// <summary>Position of index i along an axis of n points, as 0..1. n is always >= 2.</summary>
    private static float Fraction(int i, int n)
    {
        return (float)i / (n - 1);
    }

    private static int Count(Vector3Int r)
    {
        return r.x * r.y * r.z;
    }

    private static Vector3Int ClampResolution(Vector3Int r)
    {
        return new Vector3Int(
            Mathf.Clamp(r.x, MinResolution, MaxResolution),
            Mathf.Clamp(r.y, MinResolution, MaxResolution),
            Mathf.Clamp(r.z, MinResolution, MaxResolution));
    }

    // ------------------------------------------------------------------ gizmos

    private void OnDrawGizmos()
    {
        // Faint box when unselected so you can find the cage without clicking it.
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireCube(center, size);
        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDrawGizmosSelected()
    {
        EnsureOffsets();

        // 32^3 is 32k points and ~100k lines; drawing that stalls the scene view.
        if (PointCount > 4096) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.65f);

        for (int z = 0; z < resolution.z; z++)
        {
            for (int y = 0; y < resolution.y; y++)
            {
                for (int x = 0; x < resolution.x; x++)
                {
                    Vector3 p = GetPosition(x, y, z);

                    if (x + 1 < resolution.x) Gizmos.DrawLine(p, GetPosition(x + 1, y, z));
                    if (y + 1 < resolution.y) Gizmos.DrawLine(p, GetPosition(x, y + 1, z));
                    if (z + 1 < resolution.z) Gizmos.DrawLine(p, GetPosition(x, y, z + 1));
                }
            }
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}

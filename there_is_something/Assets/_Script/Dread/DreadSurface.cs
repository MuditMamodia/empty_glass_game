using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Works out where an object would age, and writes it into vertex colours.
///
/// The premise is that decay is not random - it obeys geometry. Dirt settles into crevices and
/// on upward faces. Paint wears off the exposed edges you brush past. Damp collects where air
/// does not move and runs downward in streaks. Rot spreads outward from a wound. All four are
/// computable from the mesh alone, which is exactly why this does not need an artist.
///
/// Channels written:
///   R = grime      - cavity dirt and settled dust
///   G = wear       - exposed edges where the surface is rubbed back to raw material
///   B = damp       - moisture and rot
///   A = wound      - proximity to damage (written by DreadFracture, preserved here)
/// </summary>
public static class DreadSurface
{
    [System.Serializable]
    public class Settings
    {
        [Header("Occlusion")]
        [Tooltip("Rays per vertex. 16 is grainy but instant, 64 is clean. This is the slow part.")]
        [Range(4, 128)] public int occlusionSamples = 32;

        [Tooltip("How far a ray looks for a blocker, in local units. Roughly the scale at " +
                 "which you consider something 'enclosed'.")]
        public float occlusionDistance = 0.5f;

        [Header("Grime")]
        [Range(0f, 1f)] public float grimeFromOcclusion = 0.7f;
        [Range(0f, 1f)] public float grimeFromCavity = 0.6f;

        [Tooltip("Dust settles on surfaces that face up. Set to 0 for a vertical object.")]
        [Range(0f, 1f)] public float grimeFromUpFacing = 0.5f;

        [Range(0f, 1f)] public float grimeContrast = 0.5f;

        [Header("Wear")]
        [Tooltip("Exposed convex edges get rubbed back. This is what makes an object look " +
                 "handled rather than merely dirty.")]
        [Range(0f, 1f)] public float wearFromEdges = 0.8f;

        [Range(0f, 1f)] public float wearContrast = 0.6f;

        [Header("Damp")]
        [Range(0f, 1f)] public float dampFromOcclusion = 0.6f;

        [Tooltip("Vertical streaking. Damp runs down, it does not sit in patches.")]
        [Range(0f, 1f)] public float dampStreaking = 0.7f;

        public float dampStreakScale = 8f;

        [Header("Wound")]
        [Tooltip("Keeps the damage mask DreadFracture wrote. Turn off to bake decay on an " +
                 "undamaged object.")]
        public bool preserveWoundMask = true;

        [Tooltip("Optional extra wound source in local space - rot spreads out from here too.")]
        public bool useExtraWound;
        public Vector3 extraWoundPoint;
        public float extraWoundRange = 0.3f;

        [Header("Detail")]
        [Tooltip("Breaks up the smooth geometric gradients so the result reads as material, " +
                 "not as a lighting bake.")]
        [Range(0f, 1f)] public float noiseBreakup = 0.35f;

        public float noiseScale = 12f;
        public int seed = 0;
    }

    public delegate bool ProgressCallback(string label, float fraction);

    /// <summary>
    /// Analyses the mesh and writes vertex colours in place. Returns false if cancelled.
    /// The mesh must be readable; the caller owns it (pass a copy if you care about the source).
    /// </summary>
    public static bool Bake(Mesh mesh, Settings s, ProgressCallback progress = null)
    {
        if (mesh == null || !mesh.isReadable) return false;

        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (positions.Length == 0) return false;

        if (normals == null || normals.Length != positions.Length)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        Color[] colors = mesh.colors;
        bool hadColors = colors != null && colors.Length == positions.Length;
        if (!hadColors)
        {
            colors = new Color[positions.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0f, 0f, 0f, 0f);
        }

        if (progress != null && progress("Building adjacency", 0f)) return false;

        // Weld by position first. A UV seam duplicates a vertex, and an unwelded neighbour
        // search would see those duplicates as isolated - giving a bright wrong seam straight
        // down the middle of every bake.
        int[] weldMap = BuildWeldMap(positions, out int weldedCount);
        List<int>[] neighbours = BuildNeighbours(mesh, weldMap, weldedCount);

        if (progress != null && progress("Measuring curvature", 0.1f)) return false;
        float[] curvature = ComputeCurvature(positions, normals, weldMap, neighbours, weldedCount);

        if (progress != null && progress("Casting occlusion rays", 0.2f)) return false;
        float[] occlusion = ComputeOcclusion(mesh, positions, normals, s, progress);
        if (occlusion == null) return false;

        if (progress != null && progress("Composing decay", 0.9f)) return false;

        Vector3 seedOffset = DreadNoise.SeedOffset(s.seed);

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = positions[i];
            Vector3 n = normals[i];

            float cav = Mathf.Clamp01(curvature[weldMap[i]]);        // concave
            float edge = Mathf.Clamp01(-curvature[weldMap[i]]);      // convex
            float occ = occlusion[i];
            float up = Mathf.Clamp01(n.y);

            float breakup = s.noiseBreakup * DreadNoise.Fbm(p * s.noiseScale + seedOffset, 3);

            // --- grime: cavities, enclosure, and anything facing the sky
            float grime = occ * s.grimeFromOcclusion
                        + cav * s.grimeFromCavity
                        + up * s.grimeFromUpFacing;
            grime = Contrast(Mathf.Clamp01(grime) + breakup, s.grimeContrast);

            // --- wear: exposed convex edges. Multiplied by (1 - occlusion) because a sharp
            // edge buried inside a fold never gets touched, so it never wears.
            float wear = edge * s.wearFromEdges * (1f - occ);
            wear = Contrast(Mathf.Clamp01(wear) + breakup * 0.5f, s.wearContrast);

            // --- damp: enclosed and unventilated, streaked downward. Compressing Y in the
            // noise lookup stretches the pattern vertically, which is what makes a run rather
            // than a blotch.
            float streak = DreadNoise.Fbm(
                new Vector3(p.x * s.dampStreakScale, p.y * s.dampStreakScale * 0.12f, p.z * s.dampStreakScale) + seedOffset, 2);
            float damp = occ * s.dampFromOcclusion * (1f - up);
            damp = Mathf.Clamp01(damp + streak * s.dampStreaking * damp);

            // --- wound
            float wound = s.preserveWoundMask && hadColors ? colors[i].a : 0f;
            if (s.useExtraWound)
            {
                float d = Vector3.Distance(p, s.extraWoundPoint) / Mathf.Max(s.extraWoundRange, 1e-4f);
                float inv = 1f - Mathf.Clamp01(d);
                wound = Mathf.Max(wound, inv * inv * (3f - 2f * inv));
            }

            // Rot follows the wound: damp is pushed up wherever the object is broken open.
            damp = Mathf.Clamp01(damp + wound * 0.5f);

            colors[i] = new Color(Mathf.Clamp01(grime), Mathf.Clamp01(wear), damp, Mathf.Clamp01(wound));
        }

        mesh.colors = colors;
        return true;
    }

    // ------------------------------------------------------------------ geometry analysis

    private static int[] BuildWeldMap(Vector3[] positions, out int weldedCount)
    {
        var map = new int[positions.Length];
        var lookup = new Dictionary<long, int>(positions.Length);
        weldedCount = 0;

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = positions[i];
            // Quantise to 0.1 mm and pack into one long. Cheaper than a string key and it
            // collides only for genuinely coincident vertices.
            long key = ((long)Mathf.RoundToInt(p.x * 10000f) & 0x1FFFFF)
                     | (((long)Mathf.RoundToInt(p.y * 10000f) & 0x1FFFFF) << 21)
                     | (((long)Mathf.RoundToInt(p.z * 10000f) & 0x1FFFFF) << 42);

            if (lookup.TryGetValue(key, out int existing)) { map[i] = existing; continue; }

            lookup[key] = weldedCount;
            map[i] = weldedCount;
            weldedCount++;
        }

        return map;
    }

    private static List<int>[] BuildNeighbours(Mesh mesh, int[] weldMap, int weldedCount)
    {
        var neighbours = new List<int>[weldedCount];
        for (int i = 0; i < weldedCount; i++) neighbours[i] = new List<int>(6);

        for (int sm = 0; sm < mesh.subMeshCount; sm++)
        {
            int[] tris = mesh.GetTriangles(sm);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = weldMap[tris[i]];
                int b = weldMap[tris[i + 1]];
                int c = weldMap[tris[i + 2]];

                AddOnce(neighbours[a], b); AddOnce(neighbours[a], c);
                AddOnce(neighbours[b], a); AddOnce(neighbours[b], c);
                AddOnce(neighbours[c], a); AddOnce(neighbours[c], b);
            }
        }

        return neighbours;
    }

    private static void AddOnce(List<int> list, int value)
    {
        if (!list.Contains(value)) list.Add(value);
    }

    /// <summary>
    /// Discrete mean curvature. Positive means concave (a cavity that collects), negative means
    /// convex (an edge that gets rubbed). The sign convention is the whole point, so: if the
    /// neighbours sit on the same side as the normal points, the surface curves away from you -
    /// a bowl.
    /// </summary>
    private static float[] ComputeCurvature(Vector3[] positions, Vector3[] normals,
                                            int[] weldMap, List<int>[] neighbours, int weldedCount)
    {
        // One representative position and averaged normal per welded vertex.
        var weldedPos = new Vector3[weldedCount];
        var weldedNormal = new Vector3[weldedCount];

        for (int i = 0; i < positions.Length; i++)
        {
            int w = weldMap[i];
            weldedPos[w] = positions[i];
            weldedNormal[w] += normals[i];
        }

        for (int i = 0; i < weldedCount; i++)
        {
            weldedNormal[i] = weldedNormal[i].sqrMagnitude > 1e-12f
                ? weldedNormal[i].normalized
                : Vector3.up;
        }

        var curvature = new float[weldedCount];

        for (int i = 0; i < weldedCount; i++)
        {
            List<int> ns = neighbours[i];
            if (ns.Count == 0) { curvature[i] = 0f; continue; }

            float sum = 0f;
            int used = 0;

            for (int k = 0; k < ns.Count; k++)
            {
                Vector3 d = weldedPos[ns[k]] - weldedPos[i];
                if (d.sqrMagnitude < 1e-12f) continue;
                sum += Vector3.Dot(d.normalized, weldedNormal[i]);
                used++;
            }

            // Scaled up because raw mean curvature on a smooth mesh is small; without this the
            // cavity and edge masks would need absurd multipliers to be visible.
            curvature[i] = used > 0 ? Mathf.Clamp(sum / used * 4f, -1f, 1f) : 0f;
        }

        return curvature;
    }

    /// <summary>
    /// Per-vertex ambient occlusion by raycasting a cosine-weighted hemisphere against a
    /// temporary collider built from the mesh itself. Collider.Raycast only tests that one
    /// collider, so this cannot pick up the rest of the scene and does not need a layer.
    /// </summary>
    private static float[] ComputeOcclusion(Mesh mesh, Vector3[] positions, Vector3[] normals,
                                            Settings s, ProgressCallback progress)
    {
        var occlusion = new float[positions.Length];

        var temp = new GameObject("~DreadOcclusionProbe");
        temp.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            var collider = temp.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            int sampleCount = Mathf.Clamp(s.occlusionSamples, 4, 128);
            float distance = Mathf.Max(s.occlusionDistance, 1e-3f);

            // Deterministic low-discrepancy directions, reused for every vertex. Random
            // directions would make two bakes of the same mesh differ, and re-baking after a
            // tweak would shimmer.
            var hemisphere = new Vector3[sampleCount];
            for (int i = 0; i < sampleCount; i++) hemisphere[i] = CosineHemisphere(i, sampleCount);

            int reportEvery = Mathf.Max(1, positions.Length / 50);

            for (int v = 0; v < positions.Length; v++)
            {
                if (progress != null && v % reportEvery == 0)
                {
                    if (progress("Casting occlusion rays", 0.2f + 0.7f * v / positions.Length)) return null;
                }

                Vector3 n = normals[v];
                if (n.sqrMagnitude < 1e-12f) n = Vector3.up; else n.Normalize();

                BuildBasis(n, out Vector3 tangent, out Vector3 bitangent);

                // Lift off the surface so a ray cannot immediately hit the face it started on.
                Vector3 origin = positions[v] + n * (distance * 0.002f + 1e-4f);

                int hits = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    Vector3 h = hemisphere[i];
                    Vector3 dir = tangent * h.x + bitangent * h.y + n * h.z;
                    if (collider.Raycast(new Ray(origin, dir), out _, distance)) hits++;
                }

                occlusion[v] = (float)hits / sampleCount;
            }
        }
        finally
        {
            if (Application.isPlaying) Object.Destroy(temp);
            else Object.DestroyImmediate(temp);
        }

        return occlusion;
    }

    /// <summary>Cosine-weighted hemisphere direction, z up, from the Hammersley sequence.</summary>
    private static Vector3 CosineHemisphere(int i, int count)
    {
        float u1 = (i + 0.5f) / count;
        float u2 = RadicalInverse2(i);

        float r = Mathf.Sqrt(u1);
        float theta = 2f * Mathf.PI * u2;

        return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), Mathf.Sqrt(Mathf.Max(0f, 1f - u1)));
    }

    private static float RadicalInverse2(int i)
    {
        uint bits = (uint)i;
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10f;
    }

    /// <summary>Any orthonormal basis around n. Frisvad's method, with the branch it needs.</summary>
    private static void BuildBasis(Vector3 n, out Vector3 tangent, out Vector3 bitangent)
    {
        Vector3 up = Mathf.Abs(n.y) < 0.999f ? Vector3.up : Vector3.right;
        tangent = Vector3.Cross(up, n).normalized;
        bitangent = Vector3.Cross(n, tangent);
    }

    /// <summary>S-curve around 0.5. Pushes a soft gradient toward a readable mask.</summary>
    private static float Contrast(float x, float amount)
    {
        x = Mathf.Clamp01(x);
        if (amount <= 0f) return x;
        float t = x * x * (3f - 2f * x);
        return Mathf.Lerp(x, t, Mathf.Clamp01(amount));
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One vertex, all attributes, interpolatable. Splitting a triangle creates vertices that did
/// not exist in the source, and every attribute has to be carried across or the new geometry
/// shades and textures wrongly.
/// </summary>
public struct DreadVertex
{
    public Vector3 position;
    public Vector3 normal;
    public Vector4 tangent;
    public Vector2 uv;
    public Color color;

    public static DreadVertex Lerp(DreadVertex a, DreadVertex b, float t)
    {
        return new DreadVertex
        {
            position = Vector3.Lerp(a.position, b.position, t),
            normal = Vector3.Slerp(a.normal, b.normal, t).normalized,
            tangent = Vector4.Lerp(a.tangent, b.tangent, t),
            uv = Vector2.Lerp(a.uv, b.uv, t),
            color = Color.Lerp(a.color, b.color, t),
        };
    }
}

/// <summary>
/// Splits a mesh along a noise-perturbed plane, so the result reads as torn rather than sliced.
///
/// The idea that makes this work: instead of testing vertices against a flat plane, test them
/// against a plane whose signed distance has fractal noise added to it. A vertex is "above" the
/// cut when dot(p - point, normal) + noise(p) > 0. The surface where that expression is zero is
/// a wandering, fibrous sheet, not a flat one - which is what a real break looks like. Every
/// other trick here (subdividing so the tear has room to wander, recessing the cap, sagging the
/// halves under their own weight) is in service of that one idea.
/// </summary>
public static class DreadFracture
{
    [System.Serializable]
    public class Settings
    {
        [Header("Cut Plane (object local space)")]
        public Vector3 planePoint = Vector3.zero;
        public Vector3 planeNormal = Vector3.up;

        [Header("Tear")]
        [Tooltip("How far the break wanders off the flat plane, in local units. This is the " +
                 "single most important value: 0 gives a laser cut, too much and the two " +
                 "halves stop reading as one broken object.")]
        public float tearAmplitude = 0.04f;

        [Tooltip("Detail scale of the tear. Higher means finer, more fibrous.")]
        public float tearFrequency = 5f;

        [Range(1, 6)] public int tearOctaves = 3;
        public int seed = 0;

        [Header("Subdivision")]
        [Tooltip("A triangle can only follow the tear if it is smaller than the tear's detail. " +
                 "This splits straddling triangles until they are, and leaves the rest of the " +
                 "mesh at its original density.")]
        public bool adaptiveSubdivide = true;

        [Range(0, 5)] public int maxSubdivisions = 3;
        public float targetEdgeLength = 0.04f;

        [Header("Interior")]
        public bool buildCaps = true;

        [Tooltip("How deep the exposed interior is hollowed out. A solid object with a flat " +
                 "cap reads as fake; real breaks expose a cavity.")]
        public float capDepth = 0.05f;

        public float capRoughness = 0.02f;
        [Range(1, 6)] public int capRings = 3;
        public float capUvScale = 1f;

        [Header("Aftermath")]
        [Tooltip("Local-space direction the broken ends settle in. Matter does not stay where " +
                 "it was once its support is gone.")]
        public Vector3 sagDirection = Vector3.down;

        public float sagAmount = 0f;

        [Tooltip("How far from the break the sag reaches.")]
        public float sagRange = 0.3f;

        [Tooltip("Pushes the halves apart along the cut normal, opening the wound.")]
        public float splayAmount = 0f;

        [Tooltip("Distance from the break over which the damage mask (vertex colour alpha) " +
                 "fades out. Rot and staining spread from a wound, not uniformly.")]
        public float woundRange = 0.25f;

        [Header("Output")]
        public bool recalculateBounds = true;
        public bool recalculateTangents = false;
    }

    public class Result
    {
        public Mesh above;
        public Mesh below;
        public int capSubmeshIndex = -1;
        public bool ok;
        public string error;
    }

    private struct Tri
    {
        public DreadVertex a, b, c;
        public int submesh;
    }

    private struct CutEdge
    {
        public DreadVertex p0, p1;
    }

    // ------------------------------------------------------------------ entry point

    public static Result Split(Mesh source, Settings s)
    {
        var result = new Result();

        if (source == null) { result.error = "No source mesh."; return result; }
        if (!source.isReadable)
        {
            result.error = "Mesh '" + source.name + "' is not readable. Enable Read/Write in " +
                           "its import settings.";
            return result;
        }

        Vector3 normal = s.planeNormal.sqrMagnitude < 1e-8f ? Vector3.up : s.planeNormal.normalized;
        Vector3 seedOffset = DreadNoise.SeedOffset(s.seed);

        // The noisy signed distance field. Everything downstream only ever asks this one
        // question: which side of the break is this point on, and how far?
        System.Func<Vector3, float> field = p =>
            Vector3.Dot(p - s.planePoint, normal) +
            s.tearAmplitude * DreadNoise.Fbm(p * s.tearFrequency + seedOffset, s.tearOctaves);

        List<Tri> triangles = ReadTriangles(source);
        if (triangles.Count == 0) { result.error = "Mesh has no triangles."; return result; }

        if (s.adaptiveSubdivide && s.maxSubdivisions > 0)
        {
            triangles = SubdivideAcrossCut(triangles, field, s.targetEdgeLength, s.maxSubdivisions);
        }

        int submeshCount = Mathf.Max(1, source.subMeshCount);
        int capSubmesh = s.buildCaps ? submeshCount : -1;
        int totalSubmeshes = s.buildCaps ? submeshCount + 1 : submeshCount;

        var above = new DreadMeshBuilder(totalSubmeshes);
        var below = new DreadMeshBuilder(totalSubmeshes);
        var aboveCuts = new List<CutEdge>();
        var belowCuts = new List<CutEdge>();

        foreach (Tri tri in triangles)
        {
            SplitTriangle(tri, field, above, below, aboveCuts, belowCuts);
        }

        if (above.VertexCount == 0 || below.VertexCount == 0)
        {
            result.error = "The cut plane misses the mesh - one side came out empty. Move the " +
                           "plane so it passes through the object.";
            return result;
        }

        if (s.buildCaps)
        {
            // Positive side is hollowed along +normal (into its own body), negative along -.
            BuildCap(above, capSubmesh, aboveCuts, normal, +1f, s, seedOffset);
            BuildCap(below, capSubmesh, belowCuts, normal, -1f, s, seedOffset);
        }

        ApplyAftermath(above, s, normal, +1f);
        ApplyAftermath(below, s, normal, -1f);

        result.above = above.ToMesh(source.name + "_A", s);
        result.below = below.ToMesh(source.name + "_B", s);
        result.capSubmeshIndex = capSubmesh;
        result.ok = true;
        return result;
    }

    // ------------------------------------------------------------------ source reading

    private static List<Tri> ReadTriangles(Mesh mesh)
    {
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        Vector2[] uvs = mesh.uv;
        Color[] colors = mesh.colors;

        bool hasNormals = normals != null && normals.Length == positions.Length;
        bool hasTangents = tangents != null && tangents.Length == positions.Length;
        bool hasUvs = uvs != null && uvs.Length == positions.Length;
        bool hasColors = colors != null && colors.Length == positions.Length;

        var vertices = new DreadVertex[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertices[i] = new DreadVertex
            {
                position = positions[i],
                normal = hasNormals ? normals[i] : Vector3.up,
                tangent = hasTangents ? tangents[i] : new Vector4(1f, 0f, 0f, 1f),
                uv = hasUvs ? uvs[i] : Vector2.zero,
                // Alpha is the damage mask and starts clean. If the source already had colours
                // we keep RGB and only claim alpha.
                color = hasColors ? new Color(colors[i].r, colors[i].g, colors[i].b, 0f) : new Color(1f, 1f, 1f, 0f),
            };
        }

        var list = new List<Tri>();
        int submeshCount = Mathf.Max(1, mesh.subMeshCount);

        for (int sm = 0; sm < submeshCount; sm++)
        {
            int[] indices = mesh.GetTriangles(sm);
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                list.Add(new Tri
                {
                    a = vertices[indices[i]],
                    b = vertices[indices[i + 1]],
                    c = vertices[indices[i + 2]],
                    submesh = sm,
                });
            }
        }

        return list;
    }

    // ------------------------------------------------------------------ subdivision

    /// <summary>
    /// Splits only the triangles the cut actually passes through, and only while they are
    /// coarser than the tear detail. Subdividing the whole mesh would multiply the vertex
    /// count by 4^levels for no visual gain away from the break.
    /// </summary>
    private static List<Tri> SubdivideAcrossCut(List<Tri> input, System.Func<Vector3, float> field,
                                                float targetEdge, int maxLevels)
    {
        float targetSqr = Mathf.Max(targetEdge, 1e-4f);
        targetSqr *= targetSqr;

        List<Tri> current = input;

        for (int level = 0; level < maxLevels; level++)
        {
            var next = new List<Tri>(current.Count);
            bool anySplit = false;

            foreach (Tri t in current)
            {
                float da = field(t.a.position);
                float db = field(t.b.position);
                float dc = field(t.c.position);

                bool straddles = !(da > 0f && db > 0f && dc > 0f) && !(da <= 0f && db <= 0f && dc <= 0f);

                float longest = Mathf.Max(
                    (t.a.position - t.b.position).sqrMagnitude,
                    Mathf.Max((t.b.position - t.c.position).sqrMagnitude,
                              (t.c.position - t.a.position).sqrMagnitude));

                if (!straddles || longest <= targetSqr)
                {
                    next.Add(t);
                    continue;
                }

                anySplit = true;

                DreadVertex ab = DreadVertex.Lerp(t.a, t.b, 0.5f);
                DreadVertex bc = DreadVertex.Lerp(t.b, t.c, 0.5f);
                DreadVertex ca = DreadVertex.Lerp(t.c, t.a, 0.5f);

                next.Add(new Tri { a = t.a, b = ab, c = ca, submesh = t.submesh });
                next.Add(new Tri { a = ab, b = t.b, c = bc, submesh = t.submesh });
                next.Add(new Tri { a = ca, b = bc, c = t.c, submesh = t.submesh });
                next.Add(new Tri { a = ab, b = bc, c = ca, submesh = t.submesh });
            }

            current = next;
            if (!anySplit) break;
        }

        return current;
    }

    // ------------------------------------------------------------------ the split

    private static void SplitTriangle(Tri tri, System.Func<Vector3, float> field,
                                      DreadMeshBuilder above, DreadMeshBuilder below,
                                      List<CutEdge> aboveCuts, List<CutEdge> belowCuts)
    {
        float da = field(tri.a.position);
        float db = field(tri.b.position);
        float dc = field(tri.c.position);

        bool pa = da > 0f, pb = db > 0f, pc = dc > 0f;
        int positives = (pa ? 1 : 0) + (pb ? 1 : 0) + (pc ? 1 : 0);

        if (positives == 3) { above.AddTriangle(tri.submesh, tri.a, tri.b, tri.c); return; }
        if (positives == 0) { below.AddTriangle(tri.submesh, tri.a, tri.b, tri.c); return; }

        // Rotate the winding so the vertex that is alone on its side sits first. Both remaining
        // cases then look identical to the code below, which is what keeps the winding logic
        // honest - triangle orientation must survive the split or the halves render inside out.
        DreadVertex v0, v1, v2;
        float d0, d1, d2;

        if (positives == 1)
        {
            if (pa) { v0 = tri.a; v1 = tri.b; v2 = tri.c; d0 = da; d1 = db; d2 = dc; }
            else if (pb) { v0 = tri.b; v1 = tri.c; v2 = tri.a; d0 = db; d1 = dc; d2 = da; }
            else { v0 = tri.c; v1 = tri.a; v2 = tri.b; d0 = dc; d1 = da; d2 = db; }
        }
        else // exactly one negative
        {
            if (!pa) { v0 = tri.a; v1 = tri.b; v2 = tri.c; d0 = da; d1 = db; d2 = dc; }
            else if (!pb) { v0 = tri.b; v1 = tri.c; v2 = tri.a; d0 = db; d1 = dc; d2 = da; }
            else { v0 = tri.c; v1 = tri.a; v2 = tri.b; d0 = dc; d1 = da; d2 = db; }
        }

        // P lies on edge v0->v1, Q on edge v0->v2. Both edges leave the lone vertex, so both
        // are guaranteed to cross.
        DreadVertex p = DreadVertex.Lerp(v0, v1, SolveCrossing(v0.position, v1.position, d0, d1, field));
        DreadVertex q = DreadVertex.Lerp(v0, v2, SolveCrossing(v0.position, v2.position, d0, d2, field));

        DreadMeshBuilder lone = positives == 1 ? above : below;
        DreadMeshBuilder pair = positives == 1 ? below : above;
        List<CutEdge> loneCuts = positives == 1 ? aboveCuts : belowCuts;
        List<CutEdge> pairCuts = positives == 1 ? belowCuts : aboveCuts;

        lone.AddTriangle(tri.submesh, v0, p, q);
        pair.AddTriangle(tri.submesh, p, v1, v2);
        pair.AddTriangle(tri.submesh, p, v2, q);

        // The lone piece's open edge runs P->Q, so its cap must run Q->P to face the other way.
        // The paired piece is the mirror of that.
        loneCuts.Add(new CutEdge { p0 = q, p1 = p });
        pairCuts.Add(new CutEdge { p0 = p, p1 = q });
    }

    /// <summary>
    /// Finds where along an edge the noisy field crosses zero. A straight lerp would be right
    /// for a flat plane, but the field is not linear along the edge once noise is added, so this
    /// refines with false position - same cost as a handful of noise samples, and it is what
    /// stops the tear from looking faceted.
    /// </summary>
    private static float SolveCrossing(Vector3 a, Vector3 b, float da, float db,
                                       System.Func<Vector3, float> field, int iterations = 6)
    {
        float t0 = 0f, t1 = 1f;
        float f0 = da, f1 = db;

        if (Mathf.Abs(f0 - f1) < 1e-9f) return 0.5f;

        float t = f0 / (f0 - f1);

        for (int i = 0; i < iterations; i++)
        {
            t = Mathf.Clamp(t, t0 + 1e-5f, t1 - 1e-5f);
            float f = field(Vector3.Lerp(a, b, t));

            if ((f > 0f) == (f0 > 0f)) { t0 = t; f0 = f; }
            else { t1 = t; f1 = f; }

            if (Mathf.Abs(f0 - f1) < 1e-9f) break;
            t = t0 + (t1 - t0) * (f0 / (f0 - f1));
        }

        return Mathf.Clamp01(t);
    }

    // ------------------------------------------------------------------ interior cap

    private static void BuildCap(DreadMeshBuilder builder, int submesh, List<CutEdge> cuts,
                                 Vector3 planeNormal, float side, Settings s, Vector3 seedOffset)
    {
        if (cuts.Count == 0) return;

        Vector3 centre = Vector3.zero;
        foreach (CutEdge e in cuts) centre += e.p0.position + e.p1.position;
        centre /= cuts.Count * 2f;

        // A basis on the cut plane, so the interior material gets sane UVs instead of the
        // source mesh's UVs stretched across a surface that never existed.
        Vector3 tangent = Vector3.Cross(planeNormal, Vector3.up);
        if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.Cross(planeNormal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(planeNormal, tangent).normalized;

        // Recess into this half's own body, hollowing it out.
        Vector3 inward = planeNormal * side;
        int rings = Mathf.Max(1, s.capRings);

        foreach (CutEdge edge in cuts)
        {
            for (int r = 0; r < rings; r++)
            {
                float t0 = (float)r / rings;
                float t1 = (float)(r + 1) / rings;

                Vector3 f0 = CapPoint(edge.p0.position, centre, t0, inward, s, seedOffset);
                Vector3 g0 = CapPoint(edge.p1.position, centre, t0, inward, s, seedOffset);
                Vector3 f1 = CapPoint(edge.p0.position, centre, t1, inward, s, seedOffset);
                Vector3 g1 = CapPoint(edge.p1.position, centre, t1, inward, s, seedOffset);

                if (r == rings - 1)
                {
                    AddCapTriangle(builder, submesh, f0, g0, f1, tangent, bitangent, s);
                }
                else
                {
                    AddCapTriangle(builder, submesh, f0, g0, g1, tangent, bitangent, s);
                    AddCapTriangle(builder, submesh, f0, g1, f1, tangent, bitangent, s);
                }
            }
        }
    }

    private static Vector3 CapPoint(Vector3 rim, Vector3 centre, float t, Vector3 inward,
                                    Settings s, Vector3 seedOffset)
    {
        Vector3 p = Vector3.Lerp(rim, centre, t);
        if (t <= 0f) return p;   // rim points must not move, or the two halves stop matching

        // Ridged noise, not fbm: the interior of a break is splintered, not eroded.
        float rough = DreadNoise.Ridged(p * s.tearFrequency * 2f + seedOffset, 3) - 0.5f;
        return p + inward * (s.capDepth * t + rough * s.capRoughness);
    }

    private static void AddCapTriangle(DreadMeshBuilder builder, int submesh,
                                       Vector3 a, Vector3 b, Vector3 c,
                                       Vector3 tangent, Vector3 bitangent, Settings s)
    {
        Vector3 faceNormal = Vector3.Cross(b - a, c - a);
        if (faceNormal.sqrMagnitude < 1e-12f) return;   // degenerate, skip
        faceNormal.Normalize();

        builder.AddTriangleFlat(
            submesh,
            MakeCapVertex(a, faceNormal, tangent, bitangent, s),
            MakeCapVertex(b, faceNormal, tangent, bitangent, s),
            MakeCapVertex(c, faceNormal, tangent, bitangent, s));
    }

    private static DreadVertex MakeCapVertex(Vector3 p, Vector3 faceNormal,
                                             Vector3 tangent, Vector3 bitangent, Settings s)
    {
        return new DreadVertex
        {
            position = p,
            normal = faceNormal,
            tangent = new Vector4(tangent.x, tangent.y, tangent.z, 1f),
            uv = new Vector2(Vector3.Dot(p, tangent), Vector3.Dot(p, bitangent)) * s.capUvScale,
            // Interior is the wound itself: fully marked, so decay reads strongest here.
            color = new Color(1f, 1f, 1f, 1f),
        };
    }

    // ------------------------------------------------------------------ aftermath

    /// <summary>
    /// Sag, splay and the damage mask. All three fall off with distance from the break, which is
    /// what sells it - an object that droops uniformly reads as rubber, one that droops only at
    /// the wound reads as broken.
    /// </summary>
    private static void ApplyAftermath(DreadMeshBuilder builder, Settings s, Vector3 planeNormal, float side)
    {
        bool needsSag = Mathf.Abs(s.sagAmount) > 1e-5f || Mathf.Abs(s.splayAmount) > 1e-5f;
        bool needsMask = s.woundRange > 1e-5f;
        if (!needsSag && !needsMask) return;

        Vector3 sagDir = s.sagDirection.sqrMagnitude < 1e-8f ? Vector3.down : s.sagDirection.normalized;
        float sagRange = Mathf.Max(s.sagRange, 1e-4f);
        float woundRange = Mathf.Max(s.woundRange, 1e-4f);

        for (int i = 0; i < builder.VertexCount; i++)
        {
            DreadVertex v = builder.GetVertex(i);
            float distance = Mathf.Abs(Vector3.Dot(v.position - s.planePoint, planeNormal));

            if (needsSag)
            {
                float w = Falloff(distance / sagRange);
                v.position += sagDir * (s.sagAmount * w) + planeNormal * (side * s.splayAmount * w);
            }

            if (needsMask)
            {
                float mask = Falloff(distance / woundRange);
                v.color.a = Mathf.Max(v.color.a, mask);
            }

            builder.SetVertex(i, v);
        }
    }

    /// <summary>Smooth 1 at the break falling to 0 at the range limit.</summary>
    private static float Falloff(float x)
    {
        x = Mathf.Clamp01(x);
        float inv = 1f - x;
        return inv * inv * (3f - 2f * inv);   // smoothstep, mirrored
    }
}

/// <summary>
/// Accumulates vertices and triangles and welds duplicates. Splitting produces a great many
/// coincident vertices; without welding a fractured sofa can easily come out with three times
/// the vertex count it needs.
/// </summary>
public class DreadMeshBuilder
{
    private struct Key : System.IEquatable<Key>
    {
        public int px, py, pz, nx, ny, nz, u, v;

        public bool Equals(Key o)
        {
            return px == o.px && py == o.py && pz == o.pz &&
                   nx == o.nx && ny == o.ny && nz == o.nz &&
                   u == o.u && v == o.v;
        }

        public override bool Equals(object o) { return o is Key k && Equals(k); }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = px;
                h = h * 397 ^ py; h = h * 397 ^ pz;
                h = h * 397 ^ nx; h = h * 397 ^ ny; h = h * 397 ^ nz;
                h = h * 397 ^ u; h = h * 397 ^ v;
                return h;
            }
        }
    }

    private readonly List<DreadVertex> vertices = new List<DreadVertex>();
    private readonly List<List<int>> submeshes = new List<List<int>>();
    private readonly Dictionary<Key, int> lookup = new Dictionary<Key, int>();

    public int VertexCount => vertices.Count;

    public DreadMeshBuilder(int submeshCount)
    {
        for (int i = 0; i < Mathf.Max(1, submeshCount); i++) submeshes.Add(new List<int>());
    }

    public DreadVertex GetVertex(int i) { return vertices[i]; }
    public void SetVertex(int i, DreadVertex v) { vertices[i] = v; }

    public void AddTriangle(int submesh, DreadVertex a, DreadVertex b, DreadVertex c)
    {
        List<int> target = submeshes[Mathf.Clamp(submesh, 0, submeshes.Count - 1)];
        target.Add(Add(a));
        target.Add(Add(b));
        target.Add(Add(c));
    }

    /// <summary>Adds without welding, so the three vertices keep their own face normal.</summary>
    public void AddTriangleFlat(int submesh, DreadVertex a, DreadVertex b, DreadVertex c)
    {
        List<int> target = submeshes[Mathf.Clamp(submesh, 0, submeshes.Count - 1)];
        target.Add(AddUnwelded(a));
        target.Add(AddUnwelded(b));
        target.Add(AddUnwelded(c));
    }

    private int Add(DreadVertex v)
    {
        // 1e-5 units. Tight enough that genuinely distinct vertices never merge, loose enough
        // to catch the float drift between two triangles that computed the same cut point.
        Key key = new Key
        {
            px = Mathf.RoundToInt(v.position.x * 100000f),
            py = Mathf.RoundToInt(v.position.y * 100000f),
            pz = Mathf.RoundToInt(v.position.z * 100000f),
            nx = Mathf.RoundToInt(v.normal.x * 1000f),
            ny = Mathf.RoundToInt(v.normal.y * 1000f),
            nz = Mathf.RoundToInt(v.normal.z * 1000f),
            u = Mathf.RoundToInt(v.uv.x * 100000f),
            v = Mathf.RoundToInt(v.uv.y * 100000f),
        };

        if (lookup.TryGetValue(key, out int existing)) return existing;

        vertices.Add(v);
        int index = vertices.Count - 1;
        lookup[key] = index;
        return index;
    }

    private int AddUnwelded(DreadVertex v)
    {
        vertices.Add(v);
        return vertices.Count - 1;
    }

    public Mesh ToMesh(string name, DreadFracture.Settings s)
    {
        var mesh = new Mesh { name = name };

        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        var positions = new Vector3[vertices.Count];
        var normals = new Vector3[vertices.Count];
        var tangents = new Vector4[vertices.Count];
        var uvs = new Vector2[vertices.Count];
        var colors = new Color[vertices.Count];

        for (int i = 0; i < vertices.Count; i++)
        {
            positions[i] = vertices[i].position;
            normals[i] = vertices[i].normal;
            tangents[i] = vertices[i].tangent;
            uvs[i] = vertices[i].uv;
            colors[i] = vertices[i].color;
        }

        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.uv = uvs;
        mesh.colors = colors;

        mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i, false);

        if (s == null || s.recalculateTangents) mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        return mesh;
    }
}

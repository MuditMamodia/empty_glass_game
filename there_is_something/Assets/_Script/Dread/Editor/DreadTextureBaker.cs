using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the decay masks into a real albedo texture and writes it to disk as a PNG.
///
/// This is the part that removes the artist round-trip. The vertex colours DreadSurface
/// produces describe where an object is dirty, worn, damp and wounded; this rasterises the mesh
/// in UV space and composites those masks onto a copy of the existing albedo. The output is an
/// ordinary texture asset that works with the material you already have - no custom shader, no
/// runtime cost, and your 3D artist can open the result and paint over it.
/// </summary>
public static class DreadTextureBaker
{
    [System.Serializable]
    public class Settings
    {
        [Header("Output")]
        public int resolution = 1024;

        [Tooltip("Pixels of bleed past each UV island. Without this you get hairline seams " +
                 "wherever the mesh is cut in UV space, because the GPU filters across the edge.")]
        [Range(0, 32)] public int padding = 8;

        public bool alsoWriteMaskTexture = true;

        [Header("Wear - material rubbed back to raw")]
        public Color wearColor = new Color(0.34f, 0.27f, 0.21f);
        [Range(0f, 1f)] public float wearStrength = 0.65f;

        [Header("Grime - dirt deposited on top")]
        public Color grimeColor = new Color(0.11f, 0.10f, 0.08f);
        [Range(0f, 1f)] public float grimeStrength = 0.7f;

        [Header("Damp - moisture and mould")]
        public Color dampColor = new Color(0.07f, 0.09f, 0.06f);
        [Range(0f, 1f)] public float dampStrength = 0.75f;

        [Header("Wound - exposed interior")]
        public Color woundColor = new Color(0.19f, 0.12f, 0.11f);
        [Range(0f, 1f)] public float woundStrength = 0.85f;

        [Header("Detail")]
        [Tooltip("Object-space noise grain, applied only where there is decay. This is what " +
                 "stops the bake reading as a soft gradient instead of a material.")]
        [Range(0f, 1f)] public float grain = 0.45f;

        public float grainScale = 60f;
        public int seed = 0;
    }

    /// <summary>
    /// Rasterises <paramref name="mesh"/> and returns the composited albedo. Caller owns the
    /// returned textures.
    ///
    /// Sources and tints are indexed by submesh, so a fractured mesh composites its exposed
    /// interior over the interior material rather than over the outer upholstery.
    /// </summary>
    public static bool Bake(Mesh mesh, Texture2D[] sourceAlbedos, Color[] sourceTints, Settings s,
                            out Texture2D albedo, out Texture2D masks)
    {
        albedo = null;
        masks = null;

        if (mesh == null || !mesh.isReadable) return false;

        Vector2[] uvs = mesh.uv;
        Vector3[] positions = mesh.vertices;
        Color[] colors = mesh.colors;

        if (uvs == null || uvs.Length != positions.Length) return false;
        if (colors == null || colors.Length != positions.Length) return false;

        int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(s.resolution), 64, 4096);

        var outPixels = new Color[size * size];
        var maskPixels = new Color[size * size];
        var covered = new bool[size * size];

        int submeshCount = Mathf.Max(1, mesh.subMeshCount);
        var readable = new Texture2D[submeshCount];
        var owned = new System.Collections.Generic.Dictionary<Texture2D, Texture2D>();
        Vector3 seedOffset = DreadNoise.SeedOffset(s.seed);

        try
        {
            // One readable copy per distinct source texture, not per submesh - several
            // submeshes commonly share one atlas and blitting it twice is wasted work.
            for (int sm = 0; sm < submeshCount; sm++)
            {
                Texture2D src = Pick(sourceAlbedos, sm);
                if (src == null) { readable[sm] = null; continue; }

                if (!owned.TryGetValue(src, out Texture2D copy))
                {
                    copy = MakeReadable(src, size);
                    owned[src] = copy;
                }

                readable[sm] = copy;
            }

            for (int sm = 0; sm < submeshCount; sm++)
            {
                int[] tris = mesh.GetTriangles(sm);
                Color tint = sourceTints != null && sourceTints.Length > 0
                    ? sourceTints[Mathf.Min(sm, sourceTints.Length - 1)]
                    : Color.white;

                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    RasteriseTriangle(
                        tris[i], tris[i + 1], tris[i + 2],
                        uvs, positions, colors,
                        readable[sm], tint, s, seedOffset, size,
                        outPixels, maskPixels, covered);
                }
            }

            Dilate(outPixels, covered, size, s.padding);
            if (s.alsoWriteMaskTexture) Dilate(maskPixels, covered, size, s.padding);
        }
        finally
        {
            foreach (Texture2D copy in owned.Values) Object.DestroyImmediate(copy);
        }

        albedo = new Texture2D(size, size, TextureFormat.RGBA32, true);
        albedo.SetPixels(outPixels);
        albedo.Apply();

        if (s.alsoWriteMaskTexture)
        {
            masks = new Texture2D(size, size, TextureFormat.RGBA32, true);
            masks.SetPixels(maskPixels);
            masks.Apply();
        }

        return true;
    }

    // ------------------------------------------------------------------ rasteriser

    private static void RasteriseTriangle(int i0, int i1, int i2,
                                          Vector2[] uvs, Vector3[] positions, Color[] colors,
                                          Texture2D source, Color tint, Settings s, Vector3 seedOffset,
                                          int size, Color[] outPixels, Color[] maskPixels, bool[] covered)
    {
        // UV space -> pixel space.
        Vector2 p0 = new Vector2(uvs[i0].x * size, uvs[i0].y * size);
        Vector2 p1 = new Vector2(uvs[i1].x * size, uvs[i1].y * size);
        Vector2 p2 = new Vector2(uvs[i2].x * size, uvs[i2].y * size);

        float denom = (p1.y - p2.y) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.y - p2.y);
        if (Mathf.Abs(denom) < 1e-9f) return;   // degenerate in UV space, contributes nothing

        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))) - 1);
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))) + 1);
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))) - 1);
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))) + 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                float l0 = ((p1.y - p2.y) * (px - p2.x) + (p2.x - p1.x) * (py - p2.y)) / denom;
                float l1 = ((p2.y - p0.y) * (px - p2.x) + (p0.x - p2.x) * (py - p2.y)) / denom;
                float l2 = 1f - l0 - l1;

                // A small negative tolerance makes this conservative: border texels get filled
                // by whichever triangle claims them first, and dilation cleans up the rest.
                const float Edge = -0.002f;
                if (l0 < Edge || l1 < Edge || l2 < Edge) continue;

                int index = y * size + x;
                if (covered[index]) continue;

                Color mask = colors[i0] * l0 + colors[i1] * l1 + colors[i2] * l2;
                Vector3 objectPos = positions[i0] * l0 + positions[i1] * l1 + positions[i2] * l2;

                Color baseColor = source != null
                    ? source.GetPixelBilinear(px / size, py / size) * tint
                    : tint;

                outPixels[index] = Compose(baseColor, mask, objectPos, s, seedOffset);
                maskPixels[index] = mask;
                covered[index] = true;
            }
        }
    }

    /// <summary>
    /// Layers the decay in the order it physically happens: material is worn away first, dirt
    /// is deposited on what is left, damp soaks into that, and the wound is the most recent
    /// event so it sits on top of everything.
    /// </summary>
    private static Color Compose(Color baseColor, Color mask, Vector3 objectPos,
                                 Settings s, Vector3 seedOffset)
    {
        float grime = mask.r;
        float wear = mask.g;
        float damp = mask.b;
        float wound = mask.a;

        Color c = baseColor;

        c = Color.Lerp(c, s.wearColor, wear * s.wearStrength);
        c = Color.Lerp(c, s.grimeColor, grime * s.grimeStrength);
        c = Color.Lerp(c, s.dampColor, damp * s.dampStrength);
        c = Color.Lerp(c, s.woundColor, wound * s.woundStrength);

        // Grain is sampled in object space, not UV space, so it stays continuous across UV
        // seams - the one thing that would instantly give the bake away.
        if (s.grain > 0f)
        {
            float decay = Mathf.Clamp01(grime + damp + wound);
            float n = DreadNoise.Fbm(objectPos * s.grainScale + seedOffset, 3);
            float g = 1f + n * s.grain * decay;
            c.r *= g; c.g *= g; c.b *= g;
        }

        c.r = Mathf.Clamp01(c.r);
        c.g = Mathf.Clamp01(c.g);
        c.b = Mathf.Clamp01(c.b);
        c.a = 1f;
        return c;
    }

    /// <summary>
    /// Bleeds covered texels outward. Runs one ring per pass; each pass reads the coverage from
    /// the start of that pass so the bleed grows evenly rather than streaking in scan order.
    /// </summary>
    private static void Dilate(Color[] pixels, bool[] covered, int size, int passes)
    {
        if (passes <= 0) return;

        bool[] current = (bool[])covered.Clone();

        for (int pass = 0; pass < passes; pass++)
        {
            bool[] snapshot = (bool[])current.Clone();
            bool anyFilled = false;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;
                    if (snapshot[index]) continue;

                    Color sum = Color.clear;
                    int count = 0;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= size) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= size) continue;

                            int n = ny * size + nx;
                            if (!snapshot[n]) continue;

                            sum += pixels[n];
                            count++;
                        }
                    }

                    if (count == 0) continue;

                    pixels[index] = sum / count;
                    current[index] = true;
                    anyFilled = true;
                }
            }

            if (!anyFilled) break;
        }
    }

    private static Texture2D Pick(Texture2D[] array, int index)
    {
        if (array == null || array.Length == 0) return null;
        return array[Mathf.Min(index, array.Length - 1)];
    }

    // ------------------------------------------------------------------ texture helpers

    /// <summary>
    /// Returns a readable copy at the requested size, going through the GPU so it works even
    /// when the source has Read/Write disabled or is compressed.
    /// </summary>
    public static Texture2D MakeReadable(Texture2D source, int size)
    {
        RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32,
                                                      RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;

        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            copy.Apply();
            return copy;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>Writes a texture to disk as PNG and imports it. Returns the asset path.</summary>
    public static string WritePng(Texture2D texture, string folder, string fileName, bool linear)
    {
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, fileName + ".png").Replace('\\', '/');
        File.WriteAllBytes(path, texture.EncodeToPNG());

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            // A mask must not be gamma-corrected or its values stop meaning what they say.
            importer.sRGBTexture = !linear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        return path;
    }

    /// <summary>
    /// True when the mesh's UVs leave the 0..1 square, which means the material tiles and many
    /// surface points share a texel - a bake cannot represent per-location damage there.
    /// </summary>
    public static bool HasTilingUvs(Mesh mesh, out Rect bounds)
    {
        bounds = new Rect(0f, 0f, 1f, 1f);

        Vector2[] uvs = mesh.uv;
        if (uvs == null || uvs.Length == 0) return false;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (Vector2 uv in uvs)
        {
            minX = Mathf.Min(minX, uv.x); maxX = Mathf.Max(maxX, uv.x);
            minY = Mathf.Min(minY, uv.y); maxY = Mathf.Max(maxY, uv.y);
        }

        bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
        return minX < -0.01f || minY < -0.01f || maxX > 1.01f || maxY > 1.01f;
    }
}

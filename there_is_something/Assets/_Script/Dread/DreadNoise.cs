using UnityEngine;

/// <summary>
/// Deterministic 3D gradient noise. Everything in the Dread toolkit is driven from this, so a
/// given seed always reproduces the same damage - you can re-run a bake and get the identical
/// result, and two objects with the same seed tear the same way.
///
/// Unity only ships Mathf.PerlinNoise, which is 2D. Tearing and grime are volumetric: a cut
/// surface needs noise that varies through the solid, not across a plane.
/// </summary>
public static class DreadNoise
{
    // Ken Perlin's improved-noise permutation table, doubled to avoid an index wrap in the
    // inner loop.
    private static readonly int[] Perm = new int[512];

    private static readonly int[] Source =
    {
        151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,
        240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,
        33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,77,146,
        158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,
        63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,
        109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,
        59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,152,2,44,154,163,70,221,153,
        101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,
        246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,
        49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,
        93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180
    };

    // Perlin 3D peaks at sqrt(3)/2 ~= 0.866, not 1. Scaling by its true maximum keeps the
    // amplitude fields in the rest of the toolkit meaning what they say: an amplitude of
    // 0.05 displaces by at most 5 cm, not 4.3.
    private const float PerlinNormalise = 1f / 0.8660254f;

    static DreadNoise()
    {
        for (int i = 0; i < 256; i++)
        {
            Perm[i] = Source[i];
            Perm[i + 256] = Source[i];
        }
    }

    /// <summary>Gradient noise in roughly -1..1.</summary>
    public static float Perlin(float x, float y, float z)
    {
        int xi = FloorMod(x);
        int yi = FloorMod(y);
        int zi = FloorMod(z);

        float xf = x - Mathf.Floor(x);
        float yf = y - Mathf.Floor(y);
        float zf = z - Mathf.Floor(z);

        float u = Fade(xf);
        float v = Fade(yf);
        float w = Fade(zf);

        int a = Perm[xi] + yi;
        int aa = Perm[a & 255] + zi;
        int ab = Perm[(a + 1) & 255] + zi;
        int b = Perm[(xi + 1) & 255] + yi;
        int ba = Perm[b & 255] + zi;
        int bb = Perm[(b + 1) & 255] + zi;

        float x1 = Mathf.Lerp(Grad(Perm[aa & 255], xf, yf, zf), Grad(Perm[ba & 255], xf - 1f, yf, zf), u);
        float x2 = Mathf.Lerp(Grad(Perm[ab & 255], xf, yf - 1f, zf), Grad(Perm[bb & 255], xf - 1f, yf - 1f, zf), u);
        float y1 = Mathf.Lerp(x1, x2, v);

        float x3 = Mathf.Lerp(Grad(Perm[(aa + 1) & 255], xf, yf, zf - 1f), Grad(Perm[(ba + 1) & 255], xf - 1f, yf, zf - 1f), u);
        float x4 = Mathf.Lerp(Grad(Perm[(ab + 1) & 255], xf, yf - 1f, zf - 1f), Grad(Perm[(bb + 1) & 255], xf - 1f, yf - 1f, zf - 1f), u);
        float y2 = Mathf.Lerp(x3, x4, v);

        return Mathf.Clamp(Mathf.Lerp(y1, y2, w) * PerlinNormalise, -1f, 1f);
    }

    public static float Perlin(Vector3 p)
    {
        return Perlin(p.x, p.y, p.z);
    }

    /// <summary>
    /// Fractal sum. This is what gives a tear detail at several scales at once - the big
    /// octave decides where the rip wanders, the small ones give it fibre.
    /// </summary>
    public static float Fbm(Vector3 p, int octaves = 3, float lacunarity = 2.03f, float gain = 0.5f)
    {
        octaves = Mathf.Clamp(octaves, 1, 8);

        float sum = 0f;
        float amplitude = 1f;
        float total = 0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin(p) * amplitude;
            total += amplitude;
            p *= lacunarity;
            amplitude *= gain;
        }

        return total > 0f ? sum / total : 0f;
    }

    /// <summary>
    /// Ridged fractal - sharp creases instead of smooth hills. Returns 0..1. This is the one
    /// that reads as splintered wood or torn fibre; plain fbm reads as erosion.
    /// </summary>
    public static float Ridged(Vector3 p, int octaves = 4, float lacunarity = 2.03f, float gain = 0.5f)
    {
        octaves = Mathf.Clamp(octaves, 1, 8);

        float sum = 0f;
        float amplitude = 1f;
        float total = 0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += (1f - Mathf.Abs(Perlin(p))) * amplitude;
            total += amplitude;
            p *= lacunarity;
            amplitude *= gain;
        }

        return total > 0f ? Mathf.Clamp01(sum / total) : 0f;
    }

    /// <summary>
    /// Turns a seed into a large sample-space offset. Cheaper and just as effective as
    /// rebuilding the permutation table, and it keeps the noise stateless.
    /// </summary>
    public static Vector3 SeedOffset(int seed)
    {
        // Three decorrelated hashes of the same seed. The magic numbers are odd primes; any
        // will do, they only need to not share factors.
        return new Vector3(
            Hash01(seed * 73856093) * 512f,
            Hash01(seed * 19349663 + 1) * 512f,
            Hash01(seed * 83492791 + 2) * 512f);
    }

    /// <summary>Deterministic 0..1 hash of an integer.</summary>
    public static float Hash01(int value)
    {
        uint h = (uint)value;
        h ^= 2747636419u;
        h *= 2654435769u;
        h ^= h >> 16;
        h *= 2654435769u;
        h ^= h >> 16;
        h *= 2654435769u;
        return h / 4294967295f;
    }

    private static float Fade(float t)
    {
        // 6t^5 - 15t^4 + 10t^3. Its first and second derivatives are zero at 0 and 1, which is
        // why gradient noise has no visible grid creases.
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    /// <summary>Floor into 0..255, correct for negative inputs (C# % is not).</summary>
    private static int FloorMod(float v)
    {
        int i = Mathf.FloorToInt(v) & 255;
        return i;
    }
}

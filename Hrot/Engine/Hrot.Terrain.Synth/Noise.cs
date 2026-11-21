namespace Hrot.Tools.TerrainSynth;

/// <summary>Seeded, stateless 2D value noise. All functions are pure in (seed, x, y).</summary>
public static class Noise
{
    /// <summary>Hash of an integer lattice point to [0, 1).</summary>
    public static double Lattice(ulong seed, long ix, long iy)
    {
        ulong z = seed ^ ((ulong)ix * 0x9E3779B97F4A7C15UL) ^ ((ulong)iy * 0xC2B2AE3D27D4EB4FUL);
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / 9007199254740992.0);
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    /// <summary>Smooth value noise in [0, 1], one lattice cell per unit.</summary>
    public static double Value(ulong seed, double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        long ix = (long)fx, iy = (long)fy;
        double u = Fade(x - fx), v = Fade(y - fy);
        double a = Lattice(seed, ix, iy), b = Lattice(seed, ix + 1, iy);
        double c = Lattice(seed, ix, iy + 1), d = Lattice(seed, ix + 1, iy + 1);
        double top = a + (b - a) * u;
        double bot = c + (d - c) * u;
        return top + (bot - top) * v;
    }

    /// <summary>Fractal sum of value noise, normalised to [0, 1]; wavelength halves and amplitude halves per octave.</summary>
    public static double Fbm(ulong seed, double x, double y, double wavelength, int octaves)
    {
        double sum = 0, amp = 1, ampSum = 0, w = wavelength;
        for (int o = 0; o < octaves; o++)
        {
            // a per-octave offset keeps the octaves from sharing lattice points
            sum += amp * Value(seed + (ulong)o * 0x632BE59BD9B4E019UL, x / w + 17.3 * o, y / w - 11.9 * o);
            ampSum += amp;
            amp *= 0.5;
            w *= 0.5;
        }
        return sum / ampSum;
    }

    /// <summary>Ridged noise in [0, 1]: 1 along crest lines, 0 in between.</summary>
    public static double Ridge(ulong seed, double x, double y, double wavelength) =>
        1.0 - Math.Abs(2.0 * Value(seed, x / wavelength, y / wavelength) - 1.0);
}

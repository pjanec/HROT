using System.Text;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Deterministic SplitMix64 generator; the only source of randomness in the tool.</summary>
public sealed class Rng
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private const ulong FnvOffset = 0xCBF29CE484222325UL;
    private const ulong FnvPrime = 0x100000001B3UL;

    private readonly ulong _seed;
    private ulong _state;

    public Rng(ulong seed)
    {
        _seed = seed;
        _state = seed;
    }

    public ulong NextU64()
    {
        _state += Gamma;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

    public double Range(double lo, double hi) => lo + (hi - lo) * NextDouble();

    public int Int(int loInclusive, int hiExclusive)
    {
        if (hiExclusive <= loInclusive) throw new ArgumentException("empty integer range");
        return loInclusive + (int)(NextU64() % (ulong)(hiExclusive - loInclusive));
    }

    /// <summary>
    /// An independent generator for a named stream. Seeded from the original seed and the name only (FNV-1a 64
    /// over the 8 little-endian bytes of the seed, then the UTF-8 name), so it does not depend on how much the parent drew.
    /// </summary>
    public Rng Fork(string stream)
    {
        ulong h = FnvOffset;
        for (int i = 0; i < 8; i++)
        {
            h ^= (byte)(_seed >> (8 * i));
            h *= FnvPrime;
        }
        foreach (byte b in Encoding.UTF8.GetBytes(stream))
        {
            h ^= b;
            h *= FnvPrime;
        }
        return new Rng(h);
    }
}

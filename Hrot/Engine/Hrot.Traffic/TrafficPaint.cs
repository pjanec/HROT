using System.Text;
using Hrot.Core.Tkb;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ CE-4123 (<c>docs/DESIGN_Map_3D_Mode.md</c> §6ad-3, TC4) — the paint of a SUMO car: a pure function of its vehicle id (FNV-1a), so every node, a
/// replica and a replay agree with no state on the wire, yet the traffic LOOKS random: a weighted street palette for cars, a livery set for a bus
/// or a truck. Applied where the pose is made (<see cref="TrafficMirror"/>) when SUMO gave no colour. 0xRRGGBB, never 0 (0 means "no colour").
/// </summary>
public static class TrafficPaint
{
    /// <summary>Cars: white 22 · black 18 · grey 18 · silver 14 · blue 9 · red 8 · dark green 3 · beige 3 · yellow 2 · orange 1 · brown 2.</summary>
    public static readonly (uint Rgb, int Weight)[] CarPalette =
    {
        (0xF2F2F0u, 22), (0x1B1B1Du, 18), (0x6E7176u, 18), (0xBCC0C6u, 14), (0x2F4C8Au, 9), (0xB02A2Au, 8),
        (0x23412Fu, 3), (0xC8B99Au, 3), (0xE6C229u, 2), (0xD9772Bu, 1), (0x5C4332u, 2),
    };

    /// <summary>A bus's livery: a few city colours.</summary>
    public static readonly (uint Rgb, int Weight)[] BusPalette = { (0xE6C229u, 30), (0xB02A2Au, 25), (0x2F4C8Au, 20), (0xF2F2F0u, 15), (0x2E7D4FU, 10) };

    /// <summary>A truck's fleet colours.</summary>
    public static readonly (uint Rgb, int Weight)[] TruckPalette = { (0xF2F2F0u, 40), (0x2F4C8Au, 15), (0xB02A2Au, 15), (0xE6C229u, 10), (0x6E7176u, 10), (0x2E7D4FU, 10) };

    /// <summary>The paint of vehicle <paramref name="vehicleId"/> of TKB type <paramref name="tkbType"/>.</summary>
    public static uint Of(string vehicleId, long tkbType)
    {
        var palette = tkbType == UrbanCombatTkbCatalog.TkbTrafficBus ? BusPalette
                    : tkbType == UrbanCombatTkbCatalog.TkbTrafficTruck ? TruckPalette
                    : CarPalette;
        uint h = 2166136261u;                       // FNV-1a, 32 bit
        foreach (byte b in Encoding.UTF8.GetBytes(vehicleId ?? string.Empty)) { h ^= b; h *= 16777619u; }
        h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;   // an avalanche, so the low and high bits agree
        int total = 0;
        foreach (var (_, w) in palette) total += w;
        int roll = (int)(h % (uint)total);
        foreach (var (rgb, w) in palette)
        {
            if (roll < w) return rgb;
            roll -= w;
        }
        return palette[0].Rgb;
    }
}

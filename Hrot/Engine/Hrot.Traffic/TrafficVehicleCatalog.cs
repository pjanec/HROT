using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Traffic;

namespace Hrot.Traffic;

/// <summary>One HROT car type SUMO may drive: its TKB type, SUMO class, footprint and share (AQ93 §12 T1).</summary>
public readonly record struct TrafficVehicleType(long TkbType, string Name, string VClass, float Length, float Width, float MaxSpeed, float Weight);

/// <summary>
/// ⭐ AQ93 §12 T1 (R-4056) — the TKB is the one source of what a SUMO car IS: every template carrying a
/// <see cref="TrafficVehicleDto"/> with a positive weight becomes a SUMO vehicle type, its footprint taken from the template's
/// <see cref="VehicleParametersDto"/> (so SUMO's car is our model's size). Ordered by TKB type, so every node derives the same list
/// from the same (synced, R-4018) TKB.
/// </summary>
public static class TrafficVehicleCatalog
{
    /// <summary>The default footprint of a mapped type with no <see cref="VehicleParametersDto"/> (a SUMO passenger car).</summary>
    public const float DefaultLength = 4.5f, DefaultWidth = 1.8f, DefaultMaxSpeed = 25f;

    public static IReadOnlyList<TrafficVehicleType> From(ITkbDatabase? tkb)
    {
        if (tkb is null) return Array.Empty<TrafficVehicleType>();
        var list = new List<TrafficVehicleType>();
        foreach (var t in tkb.GetAll())
        {
            if (t.GetDescriptor<TrafficVehicleDto>() is not { Weight: > 0f } v) continue;
            var p = t.GetDescriptor<VehicleParametersDto>();
            list.Add(new TrafficVehicleType(t.TkbType, t.Name, v.VClass,
                p is { Length: > 0f } ? p.Length : DefaultLength,
                p is { Width: > 0f } ? p.Width : DefaultWidth,
                p is { MaxSpeedFwd: > 0f } ? p.MaxSpeedFwd : DefaultMaxSpeed,
                v.Weight));
        }
        list.Sort((a, b) => a.TkbType.CompareTo(b.TkbType));
        return list;
    }
}

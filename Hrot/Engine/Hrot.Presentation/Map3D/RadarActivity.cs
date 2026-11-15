using Fdp.Core;
using Fdp.Toolkit.Perception.Radar;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4618 (docs/designs/air-attack/DESIGN_Radar_SAM.md §5 U-1, RS-10) — the 3-D map's question "is this entity's radar radiating?" — it spins the radar SAM's
/// search antenna (<see cref="PartRole.RadarAntenna"/>, <see cref="MotionTracker"/>). ⭐ Answered by backend's ONE reader of the replicated
/// <see cref="RadarEmission"/> (<see cref="RadarEmissions.IsOn"/>, CE-4617 B-1), so the antenna spins on every node the component reaches — a display node
/// included — and nowhere else decides it. ⛔ No allocation: a static call.
/// </summary>
public static class RadarActivity
{
    /// <summary>True when <paramref name="e"/>'s radar is radiating this frame (its replicated <see cref="RadarEmission"/> is not Off).</summary>
    public static bool IsOn(EntityRepository world, Entity e) => RadarEmissions.IsOn(world, e);
}

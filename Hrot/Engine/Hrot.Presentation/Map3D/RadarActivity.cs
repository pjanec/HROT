using Fdp.Core;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4618 (docs/designs/air-attack/DESIGN_Radar_SAM.md §5 U-1) — THE one place the 3-D map asks "is this entity's radar radiating?" — it spins the radar SAM's
/// search antenna (<see cref="PartRole.RadarAntenna"/>, <see cref="MotionTracker"/>) and is the same answer on every host once the state is replicated.
/// <para>⏸ <b>TODO (CE-4618, cross-lane):</b> backend's replicated <c>RadarEmission</c> (B-1) does not exist yet, so this answers <see langword="false"/> — the antenna
/// stands. When it lands, <see cref="IsOn"/> reads <c>RadarEmission.On</c> here and nowhere else. Until then <see cref="Source"/> is the seam a rail (or a host
/// that has the state) plugs in. ⛔ No allocation: a static method and one delegate call.</para>
/// </summary>
public static class RadarActivity
{
    /// <summary>The reader a rail or host plugs in (null ⇒ every radar is off). Process-wide: a rail restores it in a <c>finally</c>.</summary>
    public static Func<EntityRepository, Entity, bool>? Source { get; set; }

    /// <summary>True when <paramref name="e"/>'s radar is radiating this frame.</summary>
    public static bool IsOn(EntityRepository world, Entity e) => Source?.Invoke(world, e) ?? false;
}

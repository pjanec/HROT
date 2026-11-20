using Fdp.Core;

namespace Hrot.UI.Common.Map3D;

// ⭐ CE-11005 (RR-5, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — moved VERBATIM from Hrot.Presentation into the shared look
//   library so an external renderer calls the same rule the map does; the namespace is kept (as VisualFamily's was) so no caller changed — the
//   rename to Hrot.Look.* is one Roslyn rename, filed on CE-11000.

/// <summary>
/// ⭐ CE-4624 (docs/designs/minefields/DESIGN_Minefields.md §3a, MS-U4) — the 3-D map's question "is this mine clearer's flail turning?" — it spins the flail drum
/// (<see cref="PartRole.FlailDrum"/>, <see cref="MotionTracker"/>) and holds the flail loop (<c>SoundSources</c>). ⭐ ONE reader, so the drum and the sound cannot disagree and every host
/// asks the same place, as <see cref="RadarActivity"/> is for the radar.
/// <para>⭐ Bound to terrain's <c>MineClearing</c> (MS-T4) — the ONE place; the drum, the sound and the rails all go through <see cref="IsOn"/>.</para>
/// ⛔ No allocation: a static call (the test hook is a field read).
/// </summary>
public static class FlailActivity
{
    /// <summary>A rail's seam: when set, it answers instead of the stand-in (⛔ reset it to <c>null</c> in a <c>finally</c>; rails that use it share <c>Map3DFrameRail.Serial</c>).</summary>
    internal static Func<EntityRepository, Entity, bool>? Override;

    /// <summary>True when <paramref name="e"/>'s flail is turning this frame: its replicated <see cref="Fdp.Toolkit.Terrain.MineClearing"/> is active with the flail
    /// (⭐ bound to terrain's MS-T4 switch, set by the ClearLane brain). A plough or a line charge never turns the drum.</summary>
    public static bool IsOn(EntityRepository world, Entity e)
    {
        if (Override is { } over) return over(world, e);
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.MineClearing>() || !world.HasComponent<Fdp.Toolkit.Terrain.MineClearing>(e)) return false;
        ref readonly var c = ref world.GetComponentRO<Fdp.Toolkit.Terrain.MineClearing>(e);
        return c.Active != 0 && c.Tool == Fdp.Toolkit.Terrain.MineClearingTool.Flail;
    }
}

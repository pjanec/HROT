using Fdp.Core;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4624 (docs/designs/minefields/DESIGN_Minefields.md §3a, MS-U4) — the 3-D map's question "is this mine clearer's flail turning?" — it spins the flail drum
/// (<see cref="PartRole.FlailDrum"/>, <see cref="MotionTracker"/>) and holds the flail loop (<c>SoundSources</c>). ⭐ ONE reader, so the drum and the sound cannot disagree and every host
/// asks the same place, as <see cref="RadarActivity"/> is for the radar.
/// <para>⚠ <b>STAND-IN.</b> The body returns <c>false</c> until terrain's MS-T4 lands the "plough / flail active" state of the clearing vehicle (a replicated component).
/// <b>Re-bind: read terrain's flail-active state HERE, the ONE place</b> — nothing else changes (the drum, the sound and the rails all go through <see cref="IsOn"/>).</para>
/// ⛔ No allocation: a static call (the test hook is a field read).
/// </summary>
public static class FlailActivity
{
    /// <summary>A rail's seam: when set, it answers instead of the stand-in (⛔ reset it to <c>null</c> in a <c>finally</c>; rails that use it share <c>Map3DFrameRail.Serial</c>).</summary>
    internal static Func<EntityRepository, Entity, bool>? Override;

    /// <summary>True when <paramref name="e"/>'s flail is turning this frame (STAND-IN: never, until terrain's flail-active state is bound here).</summary>
    public static bool IsOn(EntityRepository world, Entity e)
        => Override is { } over && over(world, e);
}

using Fdp.Core;
using Fdp.Toolkit.Combat;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐ <c>CE-1171</c> (docs/DESIGN_People_Display.md §5 U10) — the last blow each striker struck, as the map host saw it: with what and when
/// (sim seconds). Written by <see cref="EffectSpawnSystem"/> from every <see cref="StrikeNotification"/> (backend <c>CE-3266</c>: local, or a
/// remote one from the <c>MeleeStrike</c> topic), kept beside the world (one per world), read by the 3-D bodies: a man who has just struck is
/// drawn mid-swing. ⚠ Presentation memory only — no wire; empty during a replay (the spawner is off) and after a restart, as
/// <see cref="RecentShots"/>.
/// </summary>
public sealed class StrikeTracker
{
    private readonly Dictionary<Entity, (MeleeWeapon Weapon, double At)> _last = new();

    /// <summary><paramref name="striker"/> struck with <paramref name="weapon"/> at <paramref name="at"/>.</summary>
    public void Record(Entity striker, MeleeWeapon weapon, double at) => _last[striker] = (weapon, at);

    /// <summary>Did <paramref name="striker"/> strike within <paramref name="seconds"/> before <paramref name="now"/>? With what, and when.</summary>
    public bool StruckWithin(Entity striker, double now, double seconds, out MeleeWeapon weapon, out double at)
    {
        if (_last.TryGetValue(striker, out var s) && now >= s.At && now - s.At <= seconds) { weapon = s.Weapon; at = s.At; return true; }
        weapon = default; at = 0;
        return false;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, StrikeTracker> ByWorld = new();

    /// <summary>The blows in the world (null when none has been struck since the world was made).</summary>
    public static StrikeTracker? Of(EntityRepository world) => ByWorld.TryGetValue(world, out var t) ? t : null;

    /// <summary>The world's blows, made on first use.</summary>
    public static StrikeTracker In(EntityRepository world) => ByWorld.GetValue(world, _ => new StrikeTracker());
}

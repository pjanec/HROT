using System.Numerics;
using Fdp.Core;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐ CE-1090 (docs/DESIGN_Map_3D_Mode.md §6n N-4) — the last shot each shooter fired, as the map host saw it: which mount and when (sim
/// seconds). Written by <see cref="EffectSpawnSystem"/> from every <c>WeaponFireNotification</c> (which reaches every map host — NED ingress
/// on the IG), kept beside the world (one per world), read by the 3-D bodies: a soldier who has just fired a launcher holds it on his
/// shoulder. ⚠ Presentation memory only — no wire; empty during a replay (the spawner is off) and after a restart.
/// </summary>
public sealed class RecentShots
{
    private readonly Dictionary<Entity, (int Mount, double At, Vector3 Toward)> _last = new();

    /// <summary><paramref name="shooter"/> fired mount <paramref name="mount"/> at <paramref name="at"/>. ⭐ CE-1105 (§6u) — aimed at
    /// <paramref name="toward"/> (its target or its aim point; zero when neither is known).</summary>
    public void Record(Entity shooter, int mount, double at, Vector3 toward = default) => _last[shooter] = (mount, at, toward);

    /// <summary>The last shot of <paramref name="shooter"/>, if any.</summary>
    public bool TryGet(Entity shooter, out int mount, out double at)
    {
        if (_last.TryGetValue(shooter, out var s)) { mount = s.Mount; at = s.At; return true; }
        mount = 0; at = 0;
        return false;
    }

    /// <summary>⭐ CE-1105 (§6u) — every shot fired within <paramref name="seconds"/> before <paramref name="now"/>, with where it was aimed.</summary>
    public IEnumerable<(Entity Shooter, int Mount, double At, Vector3 Toward)> Recent(double now, double seconds)
    {
        foreach (var (shooter, s) in _last)
            if (now >= s.At && now - s.At <= seconds) yield return (shooter, s.Mount, s.At, s.Toward);
    }

    /// <summary>Did <paramref name="shooter"/> fire within <paramref name="seconds"/> before <paramref name="now"/>?</summary>
    public bool FiredWithin(Entity shooter, double now, double seconds, out int mount)
        => TryGet(shooter, out mount, out double at) && now >= at && now - at <= seconds;

    // ⭐ held BESIDE the world, not in it: a managed singleton would need a central component id (FDP's GlobalComponentIds) for what is
    //   one host's drawing memory — and it must never be saved, recorded or replicated anyway
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, RecentShots> ByWorld = new();

    /// <summary>The shots in the world (null when nothing has fired since the world was made).</summary>
    public static RecentShots? Of(EntityRepository world) => ByWorld.TryGetValue(world, out var shots) ? shots : null;

    /// <summary>The world's shots, made on first use.</summary>
    public static RecentShots In(EntityRepository world) => ByWorld.GetValue(world, _ => new RecentShots());
}

using System.Numerics;
using Fdp.Core;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐ <c>CE-1215</c> (docs/DESIGN_Entity_Control.md §8j H-1, H-2) — the HITS the map host saw lately, for the first-person HUD: who hit whom,
/// where, the round's travel and damage, when (sim seconds). Written by <see cref="EffectSpawnSystem"/> from every
/// <c>DetonationNotification</c> that names a target (on every node: local, or from the <c>MunitionDetonation</c> topic — the remote one
/// carries no travel and no damage) and every <c>StrikeNotification</c> (a blow); read by the station: a HIT MARKER when the possessed unit
/// hit someone, a DAMAGE ARC toward where a hit on him came from. ⚠ Presentation memory only — no wire; empty during a replay (the spawner
/// is off), as <see cref="RecentShots"/>.
/// </summary>
public sealed class HitFeedback
{
    /// <summary>One hit. <see cref="Travel"/> zero = unknown (a remote hit, a blow); <see cref="Damage"/> 0 = unknown.</summary>
    public readonly record struct Hit(Entity Shooter, Entity Target, Vector3 At, Vector3 Travel, float Damage, double Time);

    /// <summary>How long a hit is kept (s) — the longest any HUD mark lasts.</summary>
    public const double KeepSeconds = 3.0;
    /// <summary>The hit marker's life (s).</summary>
    public const float MarkerSeconds = 0.3f;
    /// <summary>The damage arc's life (s): it fades over it.</summary>
    public const float ArcSeconds = 2f;

    private readonly List<Hit> _hits = new();

    /// <summary>A hit on <paramref name="target"/> by <paramref name="shooter"/> at <paramref name="time"/>; older ones fall away.</summary>
    public void Record(Entity shooter, Entity target, Vector3 at, Vector3 travel, float damage, double time)
    {
        if (target.IsNull) return;
        _hits.RemoveAll(h => time - h.Time > KeepSeconds || h.Time > time);
        _hits.Add(new Hit(shooter, target, at, travel, damage, time));
    }

    /// <summary>The hits within <paramref name="seconds"/> before <paramref name="now"/>.</summary>
    public IEnumerable<Hit> Recent(double now, double seconds)
    {
        foreach (var h in _hits)
            if (now >= h.Time && now - h.Time <= seconds) yield return h;
    }

    /// <summary>
    /// H-2 — where on the screen a hit's arc sits: the bearing of <paramref name="toSource"/> (from the hit man toward where it came from)
    /// relative to the camera's <paramref name="forward"/>, both flattened onto the ground, in raylib's ring degrees (0 = right, clockwise,
    /// so straight ahead is −90 and straight behind 90). Null when either is vertical or zero.
    /// </summary>
    public static float? ScreenAngle(Vector3 forward, Vector3 toSource)
    {
        var f = new Vector2(forward.X, forward.Y);
        var d = new Vector2(toSource.X, toSource.Y);
        if (f.LengthSquared() < 1e-8f || d.LengthSquared() < 1e-8f) return null;
        f = Vector2.Normalize(f);
        var right = new Vector2(f.Y, -f.X);                              // clockwise of forward, seen from above
        float clockwise = MathF.Atan2(Vector2.Dot(d, right), Vector2.Dot(d, f)) * 180f / MathF.PI;
        return -90f + clockwise;
    }

    /// <summary>
    /// H-2 — the direction a hit on <paramref name="hit"/>.Target came FROM: back along the round's travel, else toward the shooter
    /// (<paramref name="shooterAt"/>, when known) from the hit point; null when neither is known.
    /// </summary>
    public static Vector3? FromWhere(in Hit hit, Vector3? shooterAt)
        => hit.Travel != Vector3.Zero ? -hit.Travel : shooterAt is { } s && s != hit.At ? s - hit.At : null;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, HitFeedback> ByWorld = new();

    /// <summary>The hits in the world (null when none has been seen since the world was made).</summary>
    public static HitFeedback? Of(EntityRepository world) => ByWorld.TryGetValue(world, out var h) ? h : null;

    /// <summary>The world's hits, made on first use.</summary>
    public static HitFeedback In(EntityRepository world) => ByWorld.GetValue(world, _ => new HitFeedback());
}

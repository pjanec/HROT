using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Look.Effects;

/// <summary>
/// ⭐ CE-11001 (RR-1) — an automatic weapon's tracer stream, as one fire notification starts it: which gun, and where it runs to.
/// <see cref="FireStreams.Dashes(EntityRepository, in TracerStream, double, List{ValueTuple{Vector3, Vector3}})"/> turns it into the dashes of any
/// instant, from the gun's muzzle AT THAT INSTANT (a turning turret carries its stream with it, as the map has always drawn it).
/// </summary>
public struct TracerStream
{
    /// <summary>The unit that fired.</summary>
    public Entity Shooter;
    /// <summary>Its weapon mount (the fire notification's <c>WeaponIndex</c>).</summary>
    public int WeaponIndex;
    /// <summary>Where the stream runs to (<see cref="FireStreams.StreamToward"/>); zero = nothing known, the stream runs along the gun.</summary>
    public Vector3 Toward;
}

/// <summary>
/// ⭐ CE-1105 (docs/DESIGN_Map_3D_Mode.md §6u) — a burst you can see: an AUTOMATIC weapon's shot (B-1) draws, for <see cref="Window"/> seconds, a stream of
/// short tracer dashes from its muzzle toward where it aimed, running outward at <see cref="Speed"/> (B-2). Drawn from what every node already hears (the
/// fire notification) — no entity, no wire.
/// <para>⭐ CE-11001 (RR-1) — moved here from the map (<c>Hrot.UI.Common.Effects.FireStreams</c> and <c>EffectSpawnSystem.StreamToward</c>) so an external
/// renderer draws the SAME stream from the same functions: <see cref="StreamOf"/> per fire notification, then <see cref="Dashes"/> per instant. Pure,
/// sim time only, 0 B (the caller's list).</para>
/// </summary>
public static class FireStreams
{
    /// <summary>How long a stream shows after a shot (s), how far apart its dashes are and how long each is (m), how fast they run (m/s),
    /// and the furthest it reaches (m).</summary>
    public const float Window = 0.35f, Spacing = 5f, DashLength = 1.8f, Speed = 900f, MaxLength = 500f;

    /// <summary>⭐ <c>CE-1960</c> — the cosine of the widest angle (45°) between a shot's launch and the point its stream is drawn to: a fighter's lead or a
    /// rifle's spread stays inside it; a stale aim point behind the shooter does not.</summary>
    public const float StreamConeCos = 0.7071f;

    /// <summary>⭐ B-1 — a machine gun or an autocannon (the M134, the M2, the GAU-8 …); a rifle, a launcher or a big gun is one round.</summary>
    public static bool IsAutomatic(WeaponMountDto? mount) => mount?.Category is WeaponCategory.MachineGun or WeaponCategory.Autocannon;

    /// <summary>
    /// ⭐ CE-11001 (RR-1) — the stream one fire notification draws, or false when it draws none (a refused shot, a dead or unknown shooter, a weapon that is not
    /// automatic). The map records exactly this (<c>RecentShots</c>) and draws it with <see cref="Dashes"/>; an external renderer does the same.
    /// </summary>
    public static bool StreamOf(in WeaponFireNotification fire, EntityRepository world, out TracerStream stream)
    {
        stream = default;
        if (!fire.Fired || fire.Shooter == Entity.Null || !world.IsAlive(fire.Shooter) || !world.HasComponent<SimTransform>(fire.Shooter)) return false;
        if (!IsAutomatic(CombatTkb.MountOf(world, fire.Shooter, fire.WeaponIndex))) return false;
        stream = new TracerStream { Shooter = fire.Shooter, WeaponIndex = fire.WeaponIndex, Toward = TowardOf(in fire, world) };
        return true;
    }

    /// <summary>
    /// ⭐ CE-1357 — where <paramref name="fire"/>'s stream points (<see cref="StreamToward"/> over its target unit, the shooter's aim posture, and the round's
    /// own launch along the weapon's range); zero when nothing is known. The shooter must be alive and placed.
    /// </summary>
    public static Vector3 TowardOf(in WeaponFireNotification fire, EntityRepository world)
    {
        var at = world.GetComponentRO<SimTransform>(fire.Shooter).Position;
        var target = fire.Target != Entity.Null && world.IsAlive(fire.Target) && world.HasComponent<SimTransform>(fire.Target)
            ? world.GetComponentRO<SimTransform>(fire.Target).Position : WorldPoint.Zero;
        var posture = WeaponPostureOf.TryAimPoint(world, fire.Shooter, out var p) ? p : Vector3.Zero;
        var mount = CombatTkb.MountOf(world, fire.Shooter, fire.WeaponIndex);
        return StreamToward(target, posture, fire.Origin != Vector3.Zero ? fire.Origin : at, fire.Velocity, mount?.Range ?? 0f);
    }

    /// <summary>
    /// ⭐ <c>CE-1357</c> — where a shot's burst stream points: its target unit; else the shooter's aim posture; else ALONG THE ROUND'S OWN
    /// LAUNCH (<paramref name="origin"/> + its velocity's direction × the weapon's range, 50…1000 m, 300 when unstated); else nowhere.
    /// 🔴 A weapon SYSTEM (a door M134, a pintle gun) fired at a POINT has no target and no posture of its own, so the stream was drawn to
    /// (0,0,0): the bright stream from the door ran to the map's corner while only its real tracers flew to the aim. 🔒 User, 2026-10-25:
    /// "the majority of the stream of tracers is rendered to the corner of the terrain (maybe 0,0,0 or something) and just few are flying to
    /// where the aim point is". Pure.
    /// </summary>
    public static Vector3 StreamToward(Vector3 target, Vector3 posture, Vector3 origin, Vector3 velocity, float range)
    {
        // ⭐ CE-1960 — a point the round did NOT fly toward is stale (a fighter's AI attack point it has flown past, a target behind it): when the launch is
        //   known, the target / posture point is taken only inside StreamCone of it. 🔒 User, 2026-11-17: "the tracers and the fire is directed behind the
        //   aircraft, not from the nose in front, but from the cockpit to somewhere behind the aircraft, completely opposite direction".
        if (target != Vector3.Zero && Along(target)) return target;
        if (posture != Vector3.Zero && Along(posture)) return posture;
        if (velocity.LengthSquared() < 1e-4f) return Vector3.Zero;   // nothing known: no stream (Dashes draws none toward zero)
        return origin + Vector3.Normalize(velocity) * (range > 0f ? Math.Clamp(range, 50f, 1000f) : 300f);

        bool Along(Vector3 point)
        {
            var to = point - origin;
            if (velocity.LengthSquared() < 1e-4f || to.LengthSquared() < 1e-4f) return true;   // no launch known: the point is all there is
            return Vector3.Dot(Vector3.Normalize(to), Vector3.Normalize(velocity)) >= StreamConeCos;
        }
    }

    /// <summary>
    /// ⭐ B-2 — the dashes of a stream from <paramref name="muzzle"/> toward <paramref name="toward"/>, <paramref name="since"/> seconds after
    /// the shot, into <paramref name="into"/> (cleared first); and how strong it still is (1 at the shot, 0 at the window's end). A
    /// zero-length aim draws straight along <paramref name="forward"/>.
    /// </summary>
    public static float Dashes(Vector3 muzzle, Vector3 toward, Vector3 forward, double since, List<(Vector3 From, Vector3 To)> into)
    {
        into.Clear();
        if (since < 0 || since > Window) return 0f;
        var line = toward == Vector3.Zero ? Vector3.Zero : toward - muzzle;   // ⭐ CE-1357 — no aim known: along the gun, never to (0,0,0)
        float reach = line.Length();
        var dir = reach > 0.5f ? line / reach : (forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : Vector3.UnitX);
        reach = reach > 0.5f ? MathF.Min(reach, MaxLength) : MaxLength;
        float front = MathF.Min(reach, (float)since * Speed + Spacing);   // the stream's head has flown this far
        float phase = ((float)since * Speed) % Spacing;
        for (float d = phase; d < front; d += Spacing)
        {
            float end = MathF.Min(d + DashLength, front);
            if (end > d) into.Add((muzzle + dir * d, muzzle + dir * end));
        }
        return 1f - (float)(since / Window);
    }

    /// <summary>
    /// ⭐ CE-11001 (RR-1) — the dashes of <paramref name="s"/> <paramref name="since"/> seconds after its shot, from the gun's muzzle NOW (<c>Muzzle.TryOf</c>),
    /// into <paramref name="into"/> (cleared first); and how strong it still is. None (0) once the shooter is gone, no longer holds an automatic weapon on that
    /// mount, or has no muzzle. ⭐ This is exactly what the map draws (<c>ProjectileLayer.DrawStreams</c> calls it).
    /// </summary>
    public static float Dashes(EntityRepository world, in TracerStream s, double since, List<(Vector3 From, Vector3 To)> into)
        => Dashes(world, in s, since, into, out _);

    /// <summary>As <see cref="Dashes(EntityRepository, in TracerStream, double, List{ValueTuple{Vector3, Vector3}})"/>, with the muzzle the dashes start from
    /// (zero when none are drawn) — the map sizes the line by its distance to the camera.</summary>
    public static float Dashes(EntityRepository world, in TracerStream s, double since, List<(Vector3 From, Vector3 To)> into, out Vector3 muzzle)
    {
        into.Clear();
        muzzle = Vector3.Zero;
        if (!world.IsAlive(s.Shooter) || !IsAutomatic(CombatTkb.MountOf(world, s.Shooter, s.WeaponIndex))) return 0f;
        if (!Muzzle.TryOf(world, s.Shooter, s.WeaponIndex, out muzzle, out var forward)) { muzzle = Vector3.Zero; return 0f; }
        return Dashes(muzzle, s.Toward, forward, since, into);
    }

    /// <summary>
    /// ⭐ CE-11016 — the stream from STATE: a muzzle FLASH entity (the map spawns one per shot and a recording keeps it; <c>EffectAnchor</c> names the shooter,
    /// the mount and where it aimed, <c>Lifespan</c> when) of an AUTOMATIC weapon is a stream fired at <paramref name="firedAt"/> — so a replay, and the first
    /// frame after a seek, draw it with no fire events. ⚠ A flash lives 0.06–0.10 s, the stream <see cref="Window"/>: during a burst a new flash comes with
    /// every round; after the LAST round a reader that wants the full tail keeps the newest <paramref name="firedAt"/> per shooter for <see cref="Window"/>.
    /// </summary>
    public static bool StreamOfFlash(EntityRepository world, Entity flash, out TracerStream stream, out double firedAt)
    {
        stream = default; firedAt = 0;
        if (!world.IsAlive(flash) || !world.IsComponentTypeRegistered<Hrot.Map.Common.Components.EffectAnchor>()
            || !world.HasComponent<Hrot.Map.Common.Components.EffectAnchor>(flash)) return false;
        var a = world.GetComponentRO<Hrot.Map.Common.Components.EffectAnchor>(flash);
        if (a.Kind != Hrot.Map.Common.Components.EffectAnchorKind.Muzzle || !world.IsAlive(a.Shooter)) return false;
        if (!IsAutomatic(CombatTkb.MountOf(world, a.Shooter, a.WeaponIndex))) return false;
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Time.Lifespan>() || !world.HasComponent<Fdp.Toolkit.Time.Lifespan>(flash)) return false;
        firedAt = world.GetComponentRO<Fdp.Toolkit.Time.Lifespan>(flash).BornAtSimSeconds.Seconds;
        stream = new TracerStream { Shooter = a.Shooter, WeaponIndex = a.WeaponIndex, Toward = a.Toward };
        return true;
    }
}

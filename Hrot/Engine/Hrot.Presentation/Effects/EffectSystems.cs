using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common.Components;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐⭐ CE-1042 E2 — makes the realism EFFECT entities from the fire and hit events, on EVERY map host (docs/DESIGN_Visual_Effects.md
/// VE-A..VE-E, VE-J, VE-M): built by <c>MapInteractionPack</c>, scheduled with the map's interaction systems, declared in
/// <c>RequiredSystems</c> so a host that skips it is reported (R-254). Replaces IG's <c>EventToEffectSystem</c> on the map hosts.
/// <list type="bullet">
///   <item><b>Fired</b> (<see cref="WeaponFireNotification"/>): a muzzle flash ATTACHED to the shooter's weapon (<see cref="EffectAnchor"/>
///   — no position of its own, VE-J). ⛔ No tracer any more (LR-4): a tracer is a real, replicated round.</item>
///   <item><b>Hit</b> (<see cref="DetonationNotification"/>): an explosion at the hit point, world-fixed; a decal on the GROUND under it
///   (VE-I step 1, draped) — but none for a hit on a vehicle or a person (VE-M).</item>
///   <item><b>Impact</b> (<see cref="RoundImpactNotification"/>, ⭐ CE-1129): a puff and a mark on each surface a round entered, left or
///   stopped on — a wall, a roof, a floor, the ground (VE-I step 2, §10).</item>
/// </list>
/// Which effect types: the round's AMMO type's <c>Effect.Set</c>, else its calibre class (<see cref="EffectTkbCatalog.EffectsFor"/>, VE-D).
/// The events reach every node (local fire, or the NED ingress), so every host makes its own — effects are never replicated.
/// <para>⭐ W1 (§7 VE-Q, VE-R): each effect carries the generic <see cref="Lifespan"/> (made at sim now, lives the type's
/// <c>Duration</c>) — <see cref="LifespanSystem"/> removes it. Effects ARE RECORDED (never saved); ⛔ so during a replay this system is
/// OFF (<see cref="IsReplayActive"/>): the recording already holds every effect, and spawning from the replayed events would draw each
/// one twice.</para>
/// <para>It also keeps its own population honest, which no generic timer can: an attached effect whose shooter is gone ends at once
/// (VE-J), and decals over <see cref="DecalCap"/> are removed oldest first (VE-H).</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class EffectSpawnSystem : IEcsModuleSystem
{
    /// <summary>The most decals alive at once (VE-H).</summary>
    public const int DecalCap = 256;

    /// <summary>⭐ CE-1113 (§6v AD-V6) — the highest a burst may be above the ground and still scorch it (m).</summary>
    public const float DecalMaxBurstHeight = 2f;

    private readonly List<(double BornAt, Entity Entity)> _decals = new();
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    /// <summary>Effects made in the last tick, by kind — the rail's counter.</summary>
    public int Spawned { get; private set; }

    /// <summary>True while this node's world is driven by a replay ⇒ the system does nothing (VE-R); null ⇒ never.</summary>
    public Func<bool>? IsReplayActive { get; set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        Spawned = 0;
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<Lifespan>()) return;
        if (IsReplayActive?.Invoke() == true) return;
        var tkb = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
        var cmd = view.GetCommandBuffer();
        double now = SimClock.Of(world).TotalTime;
        Housekeep(world, cmd, tkb);
        MoveDecoys(world, now);   // ⭐ CE-1115

        if (world.Bus.IsRegistered<WeaponFireNotification>())
        {
            foreach (ref readonly var fire in view.ReadEvents<WeaponFireNotification>())
            {
                if (!fire.Fired) continue;   // ⭐ Q94 A-2 (R-3018) — a REFUSED shot never flew: nothing to hear, see or fear
                // ⭐ CE-1115 (§9 DV-1) — a flare / chaff release: the decoy, moving along its path; no flash, no blast, no shot
                if (Munitions.IsExpendable(tkb, fire.AmmoGuid)) { SpawnRelease(cmd, tkb, fire, now); continue; }
                if (fire.Shooter == Entity.Null || !view.IsAlive(fire.Shooter) || !view.HasComponent<SimTransform>(fire.Shooter)) continue;
                var mount = CombatTkb.MountOf(world, fire.Shooter, fire.WeaponIndex);
                // ⭐ CE-1115 (§9 DV-5, BS-1 §5.4b F3) — the round actually FIRED (the fire carries it); the mount's only from an older sender
                long fired = fire.AmmoGuid != 0 ? fire.AmmoGuid : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
                var set = EffectTkbCatalog.EffectsFor(tkb, fired, mount?.DamagePerHit ?? 0f);
                var at = view.GetComponentRO<SimTransform>(fire.Shooter).Position;
                var toward = fire.Target != Entity.Null && view.IsAlive(fire.Target) && view.HasComponent<SimTransform>(fire.Target)
                    ? view.GetComponentRO<SimTransform>(fire.Target).Position : Vector3.Zero;
                var anchor = new EffectAnchor { Shooter = fire.Shooter, WeaponIndex = fire.WeaponIndex, Toward = toward };
                // ⭐ CE-1090 — the 3-D bodies: a launcher on the shoulder; ⭐ CE-1105 — and where it was aimed (the burst stream)
                RecentShots.In(world).Record(fire.Shooter, fire.WeaponIndex, now, toward != Vector3.Zero ? toward : AimPointOf(view, fire.Shooter));
                // ⭐ CE-3248 (Civilians §7.17a TC-6) — a THROW is recorded (the body's pose reads RecentShots) and is otherwise no gunshot: no
                //   flash at a hand, no shot sound, no blast. 📐 Before: a stone has no effects of its own, so it drew the default small flash.
                if (Fdp.Toolkit.Combat.PersonHands.Threw(view, in fire)) continue;
                // ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-10) — the sound of it, queued only while an FPS station listens
                if (Hrot.UI.Common.Audio.SoundEvents.Of(world) is { } heard && mount?.Category != WeaponCategory.Bomb)
                    heard.Add(new Hrot.UI.Common.Audio.SoundEvents.Shot(fire.Shooter, fire.WeaponIndex, fire.Origin != Vector3.Zero ? fire.Origin : at,
                        fired, mount?.DamagePerHit ?? 0f, FireStreams.IsAutomatic(mount),
                        view.HasComponent<TkbIdentity>(fire.Shooter) ? view.GetComponentRO<TkbIdentity>(fire.Shooter).TkbType : 0L));
                // ⭐ CE-1094 (§6n N-10) — a bomb leaves its rack: no fire at the nose
                if (mount?.Category != WeaponCategory.Bomb) Spawn(cmd, tkb, set.MuzzleFlash, at, anchor, now);
                // ⭐ CE-1090 (§6n N-4) — a recoilless launcher throws dust BEHIND its tube; a howitzer raises a ground blast round it
                switch (Hrot.UI.Common.Map3D.WeaponKits.BlastOf(world, tkb, fire.Shooter, mount))
                {
                    case Hrot.UI.Common.Map3D.FiringBlast.Backblast:
                    {
                        var aimAt = toward != Vector3.Zero ? toward : AimPointOf(view, fire.Shooter);
                        var back = Hrot.UI.Common.Map3D.WeaponKits.BackblastPoint(at, view.GetComponentRO<SimTransform>(fire.Shooter).Rotation, aimAt);
                        Spawn(cmd, tkb, EffectTkbCatalog.Backblast, back, null, now);
                        break;
                    }
                    case Hrot.UI.Common.Map3D.FiringBlast.Ground:
                        Spawn(cmd, tkb, EffectTkbCatalog.GunBlast, at + new Vector3(0f, 0f, 0.3f), null, now);
                        break;
                }
                // ⛔ LR-4 (DESIGN_Visual_Effects.md §8.5 LR-E, R-271) — no decorative tracer any more: a tracer is a REAL round now (a
                //   tracer munition, replicated by the muscle), drawn along its actual flight by ProjectileLayer. The effect type 7131
                //   stays registered, so recordings made before this still draw theirs.
            }
        }

        // ⭐ CE-1171 (People Display §5 U10) — a blow struck (backend CE-3266, G3): the striker's swing is drawn by the 3-D bodies
        if (world.Bus.IsRegistered<Fdp.Toolkit.Combat.StrikeNotification>())
            foreach (ref readonly var strike in view.ReadEvents<Fdp.Toolkit.Combat.StrikeNotification>())
            {
                if (strike.Striker != Entity.Null) StrikeTracker.In(world).Record(strike.Striker, strike.Weapon, now);
                // ⭐ CE-1215 (§8j H-1, H-2) — a blow is a hit too: the striker's marker, the arc toward him on the one struck
                if (strike.Target != Entity.Null && view.IsAlive(strike.Target) && view.HasComponent<SimTransform>(strike.Target))
                    HitFeedback.In(world).Record(strike.Striker, strike.Target, view.GetComponentRO<SimTransform>(strike.Target).Position,
                                                 Vector3.Zero, 0f, now);
            }

        ImpactsDrawn = 0;
        InertLandings = 0;
        if (world.Bus.IsRegistered<RoundImpactNotification>())   // ⭐ CE-1129 (VE §10) — every surface a round marked
            foreach (ref readonly var impact in view.ReadEvents<RoundImpactNotification>())
                SpawnImpact(cmd, tkb, world, impact, now);

        if (world.Bus.IsRegistered<DetonationNotification>())
        {
            var ground = Fdp.Toolkit.World.WorldQuery.Of(view);
            foreach (ref readonly var hit in view.ReadEvents<DetonationNotification>())
            {
                var set = EffectTkbCatalog.EffectsFor(tkb, hit.Ammo, hit.Damage);
                var point = new Vector3(hit.HitX, hit.HitY, hit.HitZ);
                // ⭐ CE-1215 (§8j H-1, H-2) — a round that met someone: the first-person hit marker and damage arc read it
                if (hit.Target != Entity.Null)
                    HitFeedback.In(world).Record(hit.Shooter, hit.Target, point, new Vector3(hit.TravelX, hit.TravelY, hit.TravelZ), hit.Damage, now);
                // ⭐ CE-1194 — a thrown stone or blade LANDS, it does not burst: a little dust, no scorch, no bang
                if (IsInertHandItem(tkb, hit.Ammo))
                {
                    Spawn(cmd, tkb, EffectTkbCatalog.ExplosionSmall, point, null, now);   // the impact puff — dust
                    InertLandings++;
                    continue;
                }
                Spawn(cmd, tkb, set.Explosion, point, null, now);
                // ⭐ VE-M — a decal marks the static world only: none when the round hit a vehicle or a person.
                bool hitABody = hit.Target != Entity.Null && view.IsAlive(hit.Target);
                Hrot.UI.Common.Audio.SoundEvents.Of(world)?.Add(new Hrot.UI.Common.Audio.SoundEvents.Burst(point, hit.Ammo, hit.Damage, hitABody));   // ⭐ CE-1120
                if (!hitABody)
                {
                    float gz = ground?.GroundHeightAt(point.X, point.Y) ?? 0f;
                    // ⭐ CE-1113 (DESIGN_Map_3D_Mode.md §6v AD-V6) — a burst in the AIR (a SAM's proximity fuze) scorches nothing below it
                    if (point.Z - gz <= DecalMaxBurstHeight) Spawn(cmd, tkb, set.Decal, new Vector3(point.X, point.Y, gz), null, now);
                }
            }
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1129 (docs/DESIGN_Visual_Effects.md §10 IM-5 … IM-7) — each surface a round marked (<see cref="RoundImpactNotification"/>):
    /// the round's puff (its set's <c>Explosion</c> — a small-arms round's is the 7111 impact puff) and its mark (its <c>Decal</c>), the mark
    /// turned onto the face it struck (no normal ⇒ back along the travel). A stop that is not a burst adds the ricochet sound (IM-7).
    /// </summary>
    private void SpawnImpact(IEntityCommandBuffer cmd, ITkbDatabase? tkb, EntityRepository world, in RoundImpactNotification hit, double now)
    {
        var set = EffectTkbCatalog.EffectsFor(tkb, hit.Ammo, ImpactDamage(world, tkb, hit));   // ⭐ CE-1199 — sized by its round
        var point = new Vector3(hit.X, hit.Y, hit.Z);
        var normal = SurfaceOf(hit);
        Spawn(cmd, tkb, set.Explosion, point + normal * 0.05f, null, now);
        Spawn(cmd, tkb, set.Decal, point, null, now, MarkRotation(normal));
        ImpactsDrawn++;
        if (hit.Kind == (byte)ImpactKind.Stop)
            Hrot.UI.Common.Audio.SoundEvents.Of(world)?.Add(new Hrot.UI.Common.Audio.SoundEvents.Burst(point, hit.Ammo, 0f, false));   // the ricochet
    }

    /// <summary>⭐ <c>CE-1194</c> — landings drawn as dust only in the last tick (the rail's counter).</summary>
    public int InertLandings { get; private set; }

    /// <summary>
    /// ⭐ <c>CE-1194</c> — a round a hand THROWS and that carries no explosive: a hand item (<see cref="HandItemDto"/> — the stone, the thrown
    /// hatchet / spear / star, CE-3247) whose warhead says <c>Kinetic</c> or <c>Blunt</c> (no warhead stated is NOT inert: the M67 states none). Its "detonation" is where it lands: dust, not
    /// a burst. ⛔ A hand GRENADE is a hand item too, but its warhead bursts — it keeps its explosion.
    /// </summary>
    public static bool IsInertHandItem(ITkbDatabase? tkb, long ammo)
    {
        if (tkb == null || ammo == 0 || !tkb.TryGetByType(ammo, out var t) || t == null || t.GetDescriptor<HandItemDto>() == null) return false;
        return t.GetDescriptor<WarheadDto>() is { Kind: WarheadKind.Kinetic or WarheadKind.Blunt };   // ⚠ the M67 states no warhead here, and bursts
    }

    /// <summary>
    /// ⭐ <c>CE-1199</c> (VE-D) — how hard the round that made <paramref name="hit"/> strikes, so its puff and mark take its calibre class: the
    /// shooter's mount firing that ammo (its own round or one of its loadout, <see cref="CombatTkb.RoundOf(ITkbDatabase?, WeaponMountDto?, long)"/>
    /// — a tank's 120 mm round carries no damage of its own, its gun does), else the round's own number, else 0 (the small class).
    /// 📐 Before: 0 always ⇒ a tank shell's mark was a bullet's.
    /// </summary>
    public static float ImpactDamage(EntityRepository world, ITkbDatabase? tkb, in RoundImpactNotification hit)
    {
        if (hit.Ammo == 0) return 0f;
        if (!hit.Shooter.IsNull && world.IsAlive(hit.Shooter) && CombatTkb.TryGetTemplate(world, hit.Shooter, out var t)
            && t.GetDescriptor<WeaponSuiteDto>() is { } suite)
            foreach (var mount in suite.Mounts)
            {
                bool fires = unchecked((long)mount.AmmoGuid) == hit.Ammo;
                foreach (var load in mount.Loadout) fires |= unchecked((long)load.AmmoGuid) == hit.Ammo;
                if (fires) return CombatTkb.RoundOf(tkb, mount, hit.Ammo).Damage;
            }
        if (tkb != null && tkb.TryGetByType(hit.Ammo, out var ammo) && ammo != null)
            foreach (var (type, _, data) in ammo.GetAllDescriptors())
                if (type == typeof(AmmoWeaponBallisticsDto) && data is AmmoWeaponBallisticsDto b && b.Damage > 0f) return b.Damage;
        return 0f;
    }

    /// <summary>⭐ CE-1129 (IM-4) — the face a mark lies on: the impact's normal, else back along the round's travel, else up.</summary>
    public static Vector3 SurfaceOf(in RoundImpactNotification hit)
    {
        var n = new Vector3(hit.NormalX, hit.NormalY, hit.NormalZ);
        if (n.LengthSquared() > 1e-6f) return Vector3.Normalize(n);
        var back = -new Vector3(hit.TravelX, hit.TravelY, hit.TravelZ);
        return back.LengthSquared() > 1e-6f ? Vector3.Normalize(back) : Vector3.UnitZ;
    }

    /// <summary>⭐ CE-1129 (IM-6) — the rotation that turns a mark's +Z onto <paramref name="normal"/> (identity for a mark facing up).</summary>
    public static Quaternion MarkRotation(Vector3 normal)
    {
        var n = Vector3.Normalize(normal);
        float d = Vector3.Dot(Vector3.UnitZ, n);
        if (d > 0.9999f) return Quaternion.Identity;
        if (d < -0.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(d, -1f, 1f)));
    }

    /// <summary>Impact marks made in the last tick — the rail's counter (CE-1129).</summary>
    public int ImpactsDrawn { get; private set; }

    /// <summary>
    /// ⭐ CE-1115 (§9 DV-1 … DV-3) — a release's decoy: its munition's <c>Release</c> effect, born at the RELEASE (<c>FiredAt</c>) and living
    /// the expendable's <c>LifeSeconds</c> (a late viewer draws it at its true age, or not at all once burnt out), placed by
    /// <see cref="DecoyFlight.At"/> and moved by <see cref="MoveDecoys"/>.
    /// </summary>
    private void SpawnRelease(IEntityCommandBuffer cmd, ITkbDatabase? tkb, in WeaponFireNotification fire, double now)
    {
        long type = EffectTkbCatalog.EffectsFor(tkb, fire.AmmoGuid, 0f).Release;
        if (type == 0 || EffectTkbCatalog.VisualOf(tkb, type) is null || Munitions.ExpendableOf(tkb, fire.AmmoGuid) is not { } exp) return;
        double bornAt = fire.FiredAt > 0 ? fire.FiredAt : now;
        if (now - bornAt >= exp.LifeSeconds) return;
        for (int i = 0; i < Math.Max(1, fire.Quantity); i++)
        {
            var e = cmd.CreateEntity();
            cmd.AddComponent(e, new SimTransform { Position = DecoyFlight.At(fire.Origin, fire.Velocity, exp.SinkMps, (float)(now - bornAt)), Rotation = Quaternion.Identity });
            cmd.AddComponent(e, new TkbIdentity { TkbType = type });
            cmd.AddComponent(e, Lifespan.Starting(bornAt, exp.LifeSeconds));
            cmd.AddComponent(e, new EffectFlight { Origin = fire.Origin, Velocity = fire.Velocity, SinkMps = exp.SinkMps, FiredAt = bornAt });
            cmd.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());   // VE-R: recorded, never saved
            Spawned++;
        }
    }

    /// <summary>⭐ CE-1115 (§9 DV-3) — every moving effect to where its path puts it now (the SimHost's decoy is at the same point).</summary>
    private void MoveDecoys(EntityRepository world, double now)
    {
        if (!world.IsComponentTypeRegistered<EffectFlight>()) return;
        if (!ReferenceEquals(world, _flightWorld))
        {
            _flightWorld = world;
            _flights = world.Query().With<EffectFlight>().With<SimTransform>().Build();
        }
        foreach (var e in _flights!)
        {
            var f = world.GetComponentRO<EffectFlight>(e);
            ref var t = ref world.GetComponentRW<SimTransform>(e);
            t.Position = DecoyFlight.At(f.Origin, f.Velocity, f.SinkMps, (float)(now - f.FiredAt));
        }
    }

    private EntityRepository? _flightWorld;
    private EntityQuery? _flights;

    /// <summary>Where <paramref name="shooter"/>'s weapon posture aims (its target, else its point); zero when it aims at nothing.</summary>
    private static Vector3 AimPointOf(ISimulationView view, Entity shooter)
        => view is EntityRepository world && Fdp.Toolkit.Combat.WeaponPostureOf.TryAimPoint(world, shooter, out var p) ? p : Vector3.Zero;

    private void Spawn(IEntityCommandBuffer cmd, ITkbDatabase? tkb, long type, Vector3 at, EffectAnchor? anchor, double now, Quaternion? rotation = null)
    {
        if (type == 0 || EffectTkbCatalog.VisualOf(tkb, type) is not { } look) return;
        var e = cmd.CreateEntity();
        cmd.AddComponent(e, new SimTransform { Position = at, Rotation = rotation ?? Quaternion.Identity });
        cmd.AddComponent(e, new TkbIdentity { TkbType = type });
        cmd.AddComponent(e, Lifespan.Starting(now, look.Duration));
        if (anchor is { } a) cmd.AddComponent(e, a);
        cmd.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());   // VE-R: recorded, never saved with the scenario
        Spawned++;
    }

    /// <summary>VE-J — an attached effect whose shooter is gone ends now; VE-H — decals over <see cref="DecalCap"/> go, oldest first.</summary>
    private void Housekeep(EntityRepository world, IEntityCommandBuffer cmd, ITkbDatabase? tkb)
    {
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<Lifespan>().With<TkbIdentity>().Build();
        }
        bool anchored = world.IsComponentTypeRegistered<EffectAnchor>();
        _decals.Clear();
        foreach (var e in _query!)
        {
            if (anchored && world.HasComponent<EffectAnchor>(e)
                && world.GetComponentRO<EffectAnchor>(e).Shooter is var s && (s == Entity.Null || !world.IsAlive(s)))
            {
                cmd.DestroyEntity(e);
                continue;
            }
            if (EffectTkbCatalog.VisualOf(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType) is { Kind: EffectKind.Decal })
                _decals.Add((world.GetComponentRO<Lifespan>(e).BornAtSimSeconds, e));
        }
        if (_decals.Count <= DecalCap) return;
        _decals.Sort((a, b) => a.BornAt.CompareTo(b.BornAt));   // oldest first
        for (int i = 0; i < _decals.Count - DecalCap; i++)
            cmd.DestroyEntity(_decals[i].Entity);
    }
}

/// <summary>
/// ⭐⭐ W3 (docs/DESIGN_Visual_Effects.md §7 VE-P, R-269 (ui)) — <b>a vehicle that is destroyed burns:</b> when a VEHICLE's Health
/// crosses from above zero to zero, this makes two effect entities attached to its body (<see cref="EffectAnchorKind.Body"/>) —
/// flames (<see cref="EffectTkbCatalog.Fire"/>, ≈ 60 s) and smoke (<see cref="EffectTkbCatalog.Smoke"/>, ≈ 180 s, fading) — each with a
/// <see cref="Lifespan"/>, so <see cref="LifespanSystem"/> removes them. On every map host, like <see cref="EffectSpawnSystem"/>:
/// Health reaches every node, so each host makes its own.
/// <list type="bullet">
///   <item>Only a CROSSING burns: an entity first seen already destroyed (a scenario saved with a wreck, a late-joining host) does
///   not (VE-P: "a wreck loaded already dead does not burn").</item>
///   <item>Soldiers do not burn — only a type whose drawn body is a vehicle family (<see cref="EntityBodyLayer3D.Resolve"/>).</item>
///   <item>⛔ OFF during a replay (<see cref="IsReplayActive"/>, VE-R): the recording holds the flames; and its memory is dropped, so
///   coming back live cannot mistake the replayed wrecks for fresh kills.</item>
/// </list>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class WreckSpawnSystem : IEcsModuleSystem
{
    private readonly Dictionary<Entity, bool> _wasDestroyed = new();
    private readonly HashSet<Entity> _seen = new();
    private readonly List<Entity> _gone = new();
    private readonly Dictionary<long, bool> _burns = new();
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;
    private int _epoch;   // ⭐ CE-3294 — the world _wasDestroyed describes

    /// <summary>Effect entities made in the last tick — the rail's counter.</summary>
    public int Spawned { get; private set; }

    /// <summary>True while this node's world is driven by a replay ⇒ the system does nothing and forgets (VE-R); null ⇒ never.</summary>
    public Func<bool>? IsReplayActive { get; set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        Spawned = 0;
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<Lifespan>()
            || !world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() || !world.IsComponentTypeRegistered<TkbIdentity>()) return;
        if (IsReplayActive?.Invoke() == true) { _wasDestroyed.Clear(); return; }
        // ⭐ CE-3294 — a reload is a new world in the same repository (its units under the same handles): forget like a new instance
        if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch) | !ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<Fdp.Toolkit.Combat.Components.Health>().With<TkbIdentity>().With<SimTransform>().Build();
            _wasDestroyed.Clear();
            _burns.Clear();
        }
        var tkb = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
        var cmd = view.GetCommandBuffer();
        double now = SimClock.Of(world).TotalTime;
        _seen.Clear();
        foreach (var e in _query!)
        {
            _seen.Add(e);
            bool destroyed = world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current <= 0f;   // CombatLife.IsDestroyed, alive by the query
            if (destroyed && _wasDestroyed.TryGetValue(e, out bool was) && !was && Burns(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType))
            {
                var at = world.GetComponentRO<SimTransform>(e).Position;
                Spawn(cmd, tkb, EffectTkbCatalog.Fire, e, at, now);
                Spawn(cmd, tkb, EffectTkbCatalog.Smoke, e, at, now);
            }
            _wasDestroyed[e] = destroyed;
        }
        if (_wasDestroyed.Count > _seen.Count)
        {
            _gone.Clear();
            foreach (var e in _wasDestroyed.Keys) if (!_seen.Contains(e)) _gone.Add(e);
            foreach (var e in _gone) _wasDestroyed.Remove(e);
        }
    }

    /// <summary>A vehicle burns; a person, a unit with no body, a type the TKB does not know — ⭐ CE-1198 or a WEAPON SYSTEM (it goes dark,
    /// it does not burn; 🔒 user, 2026-10-19: "weapons are usually non-flammable") — does not.</summary>
    private bool Burns(ITkbDatabase? tkb, long type)
    {
        if (_burns.TryGetValue(type, out bool burns)) return burns;
        var family = Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve(tkb != null && tkb.TryGetByType(type, out var t) ? t : null).Family;
        return _burns[type] = family is not (Hrot.UI.Common.Map3D.VisualFamily.Person or Hrot.UI.Common.Map3D.VisualFamily.Unit
                                             or Hrot.UI.Common.Map3D.VisualFamily.Unknown or Hrot.UI.Common.Map3D.VisualFamily.WeaponSystem);
    }

    private void Spawn(IEntityCommandBuffer cmd, ITkbDatabase? tkb, long type, Entity wreck, Vector3 at, double now)
    {
        if (EffectTkbCatalog.VisualOf(tkb, type) is not { } look) return;
        var e = cmd.CreateEntity();
        cmd.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        cmd.AddComponent(e, new TkbIdentity { TkbType = type });
        cmd.AddComponent(e, Lifespan.Starting(now, look.Duration));
        cmd.AddComponent(e, new EffectAnchor { Shooter = wreck, Kind = EffectAnchorKind.Body });
        cmd.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());   // VE-R: recorded, never saved with the scenario
        Spawned++;
    }
}

// ⛔ RETIRED (W1, docs/DESIGN_Visual_Effects.md §7 VE-Q): `EffectLifetimeSystem` — it aged every effect by the frame delta and removed
//    it at its Duration. The generic Fdp.Toolkit.Time.LifespanSystem removes effects now (by sim time, so a replay jump or a rewind
//    is right with no catch-up); the orphan and decal-cap rules it also carried moved into EffectSpawnSystem.Housekeep.

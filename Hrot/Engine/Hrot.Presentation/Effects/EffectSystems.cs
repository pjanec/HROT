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
public sealed class EffectSpawnSystem : IEcsModuleSystem
{
    /// <summary>The most decals alive at once (VE-H).</summary>
    public const int DecalCap = 256;

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

        if (world.Bus.IsRegistered<WeaponFireNotification>())
        {
            foreach (ref readonly var fire in view.ReadEvents<WeaponFireNotification>())
            {
                if (fire.Shooter == Entity.Null || !view.IsAlive(fire.Shooter) || !view.HasComponent<SimTransform>(fire.Shooter)) continue;
                var mount = CombatTkb.MountOf(world, fire.Shooter, fire.WeaponIndex);
                var set = EffectTkbCatalog.EffectsFor(tkb, mount != null ? unchecked((long)mount.AmmoGuid) : 0L, mount?.DamagePerHit ?? 0f);
                var at = view.GetComponentRO<SimTransform>(fire.Shooter).Position;
                var toward = fire.Target != Entity.Null && view.IsAlive(fire.Target) && view.HasComponent<SimTransform>(fire.Target)
                    ? view.GetComponentRO<SimTransform>(fire.Target).Position : Vector3.Zero;
                var anchor = new EffectAnchor { Shooter = fire.Shooter, WeaponIndex = fire.WeaponIndex, Toward = toward };
                RecentShots.In(world).Record(fire.Shooter, fire.WeaponIndex, now);   // ⭐ CE-1090 — the 3-D bodies: a launcher on the shoulder
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

        if (world.Bus.IsRegistered<DetonationNotification>())
        {
            var ground = Fdp.Toolkit.World.WorldQuery.Of(view);
            foreach (ref readonly var hit in view.ReadEvents<DetonationNotification>())
            {
                var set = EffectTkbCatalog.EffectsFor(tkb, hit.Ammo, hit.Damage);
                var point = new Vector3(hit.HitX, hit.HitY, hit.HitZ);
                Spawn(cmd, tkb, set.Explosion, point, null, now);
                // ⭐ VE-M — a decal marks the static world only: none when the round hit a vehicle or a person.
                bool hitABody = hit.Target != Entity.Null && view.IsAlive(hit.Target);
                if (!hitABody)
                {
                    float gz = ground?.GroundHeightAt(point.X, point.Y) ?? 0f;
                    Spawn(cmd, tkb, set.Decal, new Vector3(point.X, point.Y, gz), null, now);
                }
            }
        }
    }

    /// <summary>Where <paramref name="shooter"/>'s weapon posture aims (its target, else its point); zero when it aims at nothing.</summary>
    private static Vector3 AimPointOf(ISimulationView view, Entity shooter)
        => view is EntityRepository world && Fdp.Toolkit.Combat.WeaponPostureOf.TryAimPoint(world, shooter, out var p) ? p : Vector3.Zero;

    private void Spawn(IEntityCommandBuffer cmd, ITkbDatabase? tkb, long type, Vector3 at, EffectAnchor? anchor, double now)
    {
        if (type == 0 || EffectTkbCatalog.VisualOf(tkb, type) is not { } look) return;
        var e = cmd.CreateEntity();
        cmd.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
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
public sealed class WreckSpawnSystem : IEcsModuleSystem
{
    private readonly Dictionary<Entity, bool> _wasDestroyed = new();
    private readonly HashSet<Entity> _seen = new();
    private readonly List<Entity> _gone = new();
    private readonly Dictionary<long, bool> _burns = new();
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

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
        if (!ReferenceEquals(world, _queryWorld))
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

    /// <summary>A vehicle burns; a person, a unit with no body, or a type the TKB does not know does not.</summary>
    private bool Burns(ITkbDatabase? tkb, long type)
    {
        if (_burns.TryGetValue(type, out bool burns)) return burns;
        var family = Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve(tkb != null && tkb.TryGetByType(type, out var t) ? t : null).Family;
        return _burns[type] = family is not (Hrot.UI.Common.Map3D.VisualFamily.Person or Hrot.UI.Common.Map3D.VisualFamily.Unit
                                             or Hrot.UI.Common.Map3D.VisualFamily.Unknown);
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

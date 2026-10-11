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
///   — no position of its own, VE-J) and, if the round has one, a tracer toward the target.</item>
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
                Spawn(cmd, tkb, set.MuzzleFlash, at, anchor, now);
                if (toward != Vector3.Zero) Spawn(cmd, tkb, set.Tracer, at, anchor, now);
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

// ⛔ RETIRED (W1, docs/DESIGN_Visual_Effects.md §7 VE-Q): `EffectLifetimeSystem` — it aged every effect by the frame delta and removed
//    it at its Duration. The generic Fdp.Toolkit.Time.LifespanSystem removes effects now (by sim time, so a replay jump or a rewind
//    is right with no catch-up); the orphan and decal-cap rules it also carried moved into EffectSpawnSystem.Housekeep.

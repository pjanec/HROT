using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
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
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class EffectSpawnSystem : IEcsModuleSystem
{
    /// <summary>Effects made in the last tick, by kind — the rail's counter.</summary>
    public int Spawned { get; private set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        Spawned = 0;
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<EffectLifetime>()) return;
        var tkb = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
        var cmd = view.GetCommandBuffer();

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
                Spawn(cmd, tkb, set.MuzzleFlash, at, anchor);
                if (toward != Vector3.Zero) Spawn(cmd, tkb, set.Tracer, at, anchor);
            }
        }

        if (world.Bus.IsRegistered<DetonationNotification>())
        {
            var ground = Fdp.Toolkit.World.WorldQuery.Of(view);
            foreach (ref readonly var hit in view.ReadEvents<DetonationNotification>())
            {
                var set = EffectTkbCatalog.EffectsFor(tkb, hit.Ammo, hit.Damage);
                var point = new Vector3(hit.HitX, hit.HitY, hit.HitZ);
                Spawn(cmd, tkb, set.Explosion, point, null);
                // ⭐ VE-M — a decal marks the static world only: none when the round hit a vehicle or a person.
                bool hitABody = hit.Target != Entity.Null && view.IsAlive(hit.Target);
                if (!hitABody)
                {
                    float gz = ground?.GroundHeightAt(point.X, point.Y) ?? 0f;
                    Spawn(cmd, tkb, set.Decal, new Vector3(point.X, point.Y, gz), null);
                }
            }
        }
    }

    private void Spawn(IEntityCommandBuffer cmd, ITkbDatabase? tkb, long type, Vector3 at, EffectAnchor? anchor)
    {
        if (type == 0 || EffectTkbCatalog.VisualOf(tkb, type) is not { } look) return;
        var e = cmd.CreateEntity();
        cmd.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        cmd.AddComponent(e, new TkbIdentity { TkbType = type });
        cmd.AddComponent(e, new EffectLifetime { Age = 0f, Duration = look.Duration });
        if (anchor is { } a) cmd.AddComponent(e, a);
        cmd.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());   // VE-B: never saved with the scenario
        Spawned++;
    }
}

/// <summary>
/// ⭐ CE-1042 E2 (VE-G, VE-H) — ages every effect by SIMULATION time and removes it when its life ends; an attached effect whose
/// shooter is gone ends at once (VE-J); decals over <see cref="DecalCap"/> are removed oldest first, so a long firefight cannot grow
/// without bound.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class EffectLifetimeSystem : IEcsModuleSystem
{
    /// <summary>The most decals alive at once (VE-H).</summary>
    public const int DecalCap = 256;

    private readonly List<(float Age, Entity Entity)> _decals = new();
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    /// <summary>Live effects after the last tick — the rail's counter.</summary>
    public int Alive { get; private set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        Alive = 0;
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<EffectLifetime>()) return;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<EffectLifetime>().Build();
        }
        var tkb = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
        var cmd = view.GetCommandBuffer();
        bool anchored = world.IsComponentTypeRegistered<EffectAnchor>();
        _decals.Clear();
        float dt = deltaTime > 0f && float.IsFinite(deltaTime) ? deltaTime : 0f;

        foreach (var e in _query!)
        {
            var life = world.GetComponentRO<EffectLifetime>(e);
            life.Age += dt;
            bool orphan = anchored && world.HasComponent<EffectAnchor>(e)
                          && world.GetComponentRO<EffectAnchor>(e).Shooter is var s && (s == Entity.Null || !world.IsAlive(s));
            if (life.Age >= life.Duration || orphan)
            {
                cmd.DestroyEntity(e);
                continue;
            }
            cmd.SetComponent(e, life);
            Alive++;
            if (world.HasComponent<TkbIdentity>(e)
                && EffectTkbCatalog.VisualOf(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType) is { Kind: EffectKind.Decal })
                _decals.Add((life.Age, e));
        }

        if (_decals.Count > DecalCap)
        {
            _decals.Sort((a, b) => b.Age.CompareTo(a.Age));   // oldest first
            for (int i = 0; i < _decals.Count - DecalCap; i++)
            {
                cmd.DestroyEntity(_decals[i].Entity);
                Alive--;
            }
        }
    }
}

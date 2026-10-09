using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Terrain;

namespace Hrot.Map.Common.Services;

/// <summary>
/// ⭐ <c>CE-3136</c> P-7a (R-243) — a node's handle on its <see cref="TerrainResidency"/> for the obstacle bake, published as a managed
/// world singleton by every terrain commit / unload. ⭐ Its ABSENCE is meaningful: the node holds no terrain, so there is nothing to
/// bake and an obstacle is acked at once (the "a host with nothing to make resident still acks" rule of <c>TerrainAssetHandler</c>).
/// </summary>
[ComponentId(GlobalComponentIds.StaticObstacleBakery)]
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
public sealed class StaticObstacleBakery
{
    internal StaticObstacleBakery(TerrainResidency residency) => Residency = residency;

    internal TerrainResidency Residency { get; }

    /// <summary>The residency's base version (bumped by every terrain commit / unload).</summary>
    public int BaseVersion => Residency.BaseVersion;
}

/// <summary>
/// ⭐⭐ <c>CE-3136</c> P-7a (R-243, R-244; 📄 docs/DESIGN_Peek_And_Fire.md §9.2 O2, §9.4) — makes every static obstacle on this node
/// TERRAIN, and holds it out of the simulation until it is: the terrain lifecycle participant (<see cref="TerrainObstacles.LifecycleModuleId"/>).
/// <list type="number">
/// <item>Each frame it collects the static obstacles (Active and still Constructing) as prisms and hashes the set.</item>
/// <item>When the set differs from the last baked one (or the terrain under it changed), it waits until the set has been quiet for
/// <see cref="DebounceSeconds"/> of WALL-CLOCK time (R-244 — it must also fire while paused or loading), then bakes it ONCE in the
/// background: world + cover + touched navmesh tiles (<see cref="TerrainResidency.BakeObstacles"/>).</item>
/// <item>On completion it commits the bake (main thread) and only THEN acks every obstacle of the batch — so an obstacle is never
/// in the simulation before the world it belongs to, and a scenario load (which waits for nothing Constructing) finishes on the baked
/// world, whatever the machine's speed.</item>
/// <item>A change arriving while a bake runs is picked up when it ends (coalesced: one running, one pending at most).</item>
/// </list>
/// ⚠ A node with no terrain (no <see cref="StaticObstacleBakery"/> singleton) acks at once.
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
[SingleInstance]
public sealed class StaticObstacleBakeSystem : IEcsModuleSystem
{
    /// <summary>R-244 — the wall-clock quiet period before a bake starts.</summary>
    public const double DebounceSeconds = 0.5;

    private readonly Func<double> _wallSeconds;
    private readonly List<TerrainPrism> _prisms = new();
    private readonly List<Entity> _pending = new();

    private Task<TerrainResidency.ObstacleBake>? _running;
    private TerrainResidency? _runningOn;
    private List<Entity> _runningBatch = new();
    private ulong _runningSignature;

    private ulong _bakedSignature = EmptySignature;
    private int _bakedBase = int.MinValue;
    private ulong _seenSignature = EmptySignature;
    private double _seenAt;

    private const ulong EmptySignature = 1469598103934665603UL;

    /// <param name="wallSeconds">The wall clock, in seconds (R-244). Null ⇒ a <see cref="Stopwatch"/>; a test passes its own.</param>
    public StaticObstacleBakeSystem(Func<double>? wallSeconds = null)
    {
        if (wallSeconds != null) _wallSeconds = wallSeconds;
        else
        {
            var sw = Stopwatch.StartNew();
            _wallSeconds = () => sw.Elapsed.TotalSeconds;
        }
    }

    /// <summary>Bakes committed (diagnostics / rails).</summary>
    public int Bakes { get; private set; }

    /// <summary>The last bake's wall-clock duration (ms; diagnostics).</summary>
    public long LastBakeMilliseconds { get; private set; }

    /// <summary>True while a bake runs in the background.</summary>
    public bool Baking => _running != null;

    public void Execute(ISimulationView view, float dt)
    {
        if (view is not EntityRepository world) return;
        if (!world.IsComponentTypeRegistered<StaticObstacle>()) return;

        var bakery = world.HasSingletonManaged<StaticObstacleBakery>() ? world.GetSingletonManaged<StaticObstacleBakery>() : null;
        Collect(world);

        if (bakery == null)
        {
            // No terrain on this node: nothing to bake — the obstacle needs no wait here.
            foreach (var e in _pending) Ack(world, e);
            return;
        }

        if (_running != null)
        {
            if (!_running.IsCompleted) return;
            Finish(world);
        }

        ulong signature = Signature();
        bool changed = signature != _bakedSignature || (_prisms.Count > 0 && bakery.BaseVersion != _bakedBase);
        if (!changed)
        {
            foreach (var e in _pending) Ack(world, e);   // already in the baked world (e.g. it arrived while that bake ran)
            return;
        }

        double now = _wallSeconds();
        if (signature != _seenSignature) { _seenSignature = signature; _seenAt = now; return; }
        if (now - _seenAt < DebounceSeconds) return;

        // ⭐ ONE bake for everything that has gathered — a whole scenario's obstacles arrive within the quiet period.
        var prisms = new List<TerrainPrism>(_prisms);
        var residency = bakery.Residency;
        _runningOn = residency;
        _runningBatch = new List<Entity>(_pending);
        _runningSignature = signature;
        _running = Task.Run(() => residency.BakeObstacles(prisms));
    }

    private void Finish(EntityRepository world)
    {
        var task = _running!;
        var batch = _runningBatch;
        var residency = _runningOn!;
        _running = null; _runningOn = null; _runningBatch = new List<Entity>();

        if (task.IsFaulted)
        {
            // ⛔ Never silent: the obstacles still join the simulation (an obstacle must not hang a scenario load), but this
            //    node's terrain does not hold them — said loudly.
            FdpLog<StaticObstacleBakeSystem>.Error(
                $"[Obstacles] Bake of {batch.Count} obstacle(s) FAILED — they are live but NOT in this node's terrain: {task.Exception?.GetBaseException().Message}");
            foreach (var e in batch) Ack(world, e);
            return;
        }

        var bake = task.Result;
        if (!residency.CommitObstacles(world, bake))
            return;   // the terrain changed under the bake: the next frame bakes again over the new base (the batch keeps waiting)

        _bakedSignature = _runningSignature;
        _bakedBase = residency.BaseVersion;
        Bakes++;
        LastBakeMilliseconds = bake.Milliseconds;
        FdpLog<StaticObstacleBakeSystem>.Info(
            $"[Obstacles] Baked {bake.Obstacles} static obstacle(s) into the terrain in {bake.Milliseconds} ms; acking {batch.Count}.");
        foreach (var e in batch) Ack(world, e);
    }

    private void Collect(EntityRepository world)
    {
        _prisms.Clear();
        _pending.Clear();
        var library = (world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>()?.Materials : null) ?? TerrainMaterialLibrary.Shared;

        foreach (var e in world.Query().With<StaticObstacle>().With<ObstacleShape>().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build())
        {
            var state = world.GetLifecycleState(e);
            if (state != EntityLifecycle.Active && state != EntityLifecycle.Constructing) continue;
            if (state == EntityLifecycle.Constructing) _pending.Add(e);

            ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
            ref readonly var shape = ref world.GetComponentRO<ObstacleShape>(e);
            string materialName = world.GetComponentRO<StaticObstacle>(e).Material.ToString() ?? "concrete";
            var material = TerrainObstacles.MaterialOf(library, materialName, out bool known);
            if (!known)
                FdpLog<StaticObstacleBakeSystem>.Warn($"[Obstacles] {e}: unknown material '{materialName}' — baked as concrete.");
            _prisms.Add(TerrainObstacles.PrismOf(t.Position, TerrainObstacles.Yaw(t.Rotation), shape, material,
                TerrainObstacles.LabelPrefix + e.Index));
        }
    }

    private ulong Signature()
    {
        // FNV-1a over every prism's identity and geometry, in query order (stable within a world).
        // ⭐ Quantised to 1 cm: a REPLICA's position arrives through the geo transform with float noise (105.000015), and an
        //   exact-bits hash re-baked the world on every position refresh — measured live on bt-window-duel (~150 bakes, CE-3136 P-8).
        ulong h = EmptySignature;
        void Mix(float f) { h ^= (ulong)(long)MathF.Round(f * 100f); h *= 1099511628211UL; }
        foreach (var p in _prisms)
        {
            foreach (var v in p.Footprint) { Mix(v.X); Mix(v.Y); }
            Mix(p.BaseZ); Mix(p.TopZ);
            h ^= (ulong)(p.Material?.Name.GetHashCode() ?? 0); h *= 1099511628211UL;
        }
        return h;
    }

    private static void Ack(EntityRepository world, Entity e)
        => world.Bus.Publish(new ConstructionAck { Entity = e, ModuleId = TerrainObstacles.LifecycleModuleId, Success = true });
}

using System.Diagnostics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;

namespace Fdp.Examples.ThreadedKernel;

// ⚠ PoC: ids from the held-back top of the 512 range — this process registers nothing else there
[ComponentId(506)] public struct Position { public float X, Y; }
[ComponentId(507)] public struct Velocity { public float X, Y; }
/// <summary>Stands in for a brain: how many units of busy work this entity costs per simulation tick.</summary>
/// <summary>A transient event: an entity bounced off the map edge (the kind of thing an effect is drawn for).</summary>
[EventId(9001)] public struct Bounce { public Entity Who; public float X, Y; }

/// <summary>Stands in for the dozens of components a real HROT entity carries (~512 B), part of it rewritten every tick.</summary>
[ComponentId(509)] public unsafe struct Payload { public fixed byte Bytes[512]; }
[ComponentId(508)] public struct Brain { public int Work; public float Mood; }

/// <summary>The schema, shared by the live world and every pooled snapshot (a snapshot must register the same types).</summary>
public static class Schema
{
    public static void Register(EntityRepository repo)
    {
        repo.RegisterComponent<GlobalTime>();
        repo.Bus.Register<Bounce>();
        repo.RegisterComponent<Position>();
        repo.RegisterComponent<Velocity>();
        repo.RegisterComponent<Brain>();
        repo.RegisterComponent<Payload>();
    }
}

/// <summary>Knobs the UI changes; read by the worker. Plain volatile fields — a PoC, not a contract.</summary>
public sealed class SimSettings
{
    public long BouncesPublished;   // worker-side count, to prove the presentation sees each event exactly once
    public volatile int WorkPerEntity = 200;     // iterations of busy math per entity per tick (the "brain cost")
    public volatile int TargetEntities;
    public volatile bool Paused;
    public volatile bool WithPayload;
}

/// <summary>
/// The SIMULATION module — the kernel's ordinary synchronous module, ticked on the WORKER thread with direct access to World A.
/// Its systems: drain the presentation's commands (Input), move + "think" (Simulation), publish a snapshot (Export).
/// </summary>
[TickMode(TickPolicy.EveryFrame)]
public sealed class SimulationModule : IEcsModule
{
    private readonly SimSettings _settings;
    private readonly CommandIngress _ingress;
    private readonly SnapshotPort _port;
    public SimulationModule(SimSettings settings, CommandIngress ingress, SnapshotPort port) { _settings = settings; _ingress = ingress; _port = port; }
    public string Name => "Simulation";
    public void Tick(ISimulationView view, float deltaTime) { }   // all work is in the registered systems
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new CommandIngressSystem(_ingress));
        registry.RegisterSystem(new PopulationSystem(_settings));
        registry.RegisterSystem(new BrainSystem(_settings));
        registry.RegisterSystem(new MoveSystem(_settings));
        registry.RegisterSystem(new PublishSystem(_port));
    }
}

[UpdateInPhase(SystemPhase.Input)][TickMode(TickPolicy.EveryFrame)]
public sealed class CommandIngressSystem : IEcsModuleSystem
{
    private readonly CommandIngress _ingress;
    public CommandIngressSystem(CommandIngress ingress) => _ingress = ingress;
    public void Execute(ISimulationView view, float deltaTime) => _ingress.Drain((EntityRepository)view);
}

/// <summary>Grows / shrinks the population to the UI's target (on the worker, so creation stays on World A's thread).</summary>
[UpdateInPhase(SystemPhase.Input)][TickMode(TickPolicy.EveryFrame)]
public sealed class PopulationSystem : IEcsModuleSystem
{
    private readonly SimSettings _s;
    private readonly Random _rng = new(1);
    private readonly List<Entity> _alive = new();
    public PopulationSystem(SimSettings s) => _s = s;
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        int target = _s.TargetEntities;
        while (_alive.Count < target)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = (float)_rng.NextDouble() * 1000f, Y = (float)_rng.NextDouble() * 1000f });
            var a = (float)(_rng.NextDouble() * Math.PI * 2);
            repo.AddComponent(e, new Velocity { X = MathF.Cos(a) * 20f, Y = MathF.Sin(a) * 20f });
            repo.AddComponent(e, new Brain { Work = 1 });
            if (_s.WithPayload) repo.AddComponent(e, new Payload());
            _alive.Add(e);
        }
        while (_alive.Count > target)
        {
            repo.DestroyEntity(_alive[^1]);
            _alive.RemoveAt(_alive.Count - 1);
        }
    }
}

/// <summary>The "brains": busy work per entity, scaled by the UI's knob — the cost that makes the simulation slow.</summary>
[UpdateInPhase(SystemPhase.Simulation)][TickMode(TickPolicy.EveryFrame)]
public sealed class BrainSystem : IEcsModuleSystem
{
    private EntityQuery? _pq;
    private readonly SimSettings _s;
    private EntityQuery? _q;
    public BrainSystem(SimSettings s) => _s = s;
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (_s.Paused) return;
        var repo = (EntityRepository)view;
        _q ??= repo.Query().With<Brain>().With<Velocity>().Build();
        int work = _s.WorkPerEntity;
        foreach (var e in _q)
        {
            ref var b = ref repo.GetComponentRW<Brain>(e);
            float x = b.Mood;
            for (int i = 0; i < work; i++) x = MathF.Sin(x + i * 0.001f) * 0.999f;   // the "thinking"
            b.Mood = x;
            if (_s.WithPayload && repo.HasComponent<Payload>(e)) unsafe { ref var pl = ref repo.GetComponentRW<Payload>(e); pl.Bytes[(int)(repo.SimulationTime * 60) & 511]++; }
            if (x > 0.995f)   // rarely: turn around
            {
                ref var v = ref repo.GetComponentRW<Velocity>(e);
                v.X = -v.X; v.Y = -v.Y;
            }
        }
    }
}

[UpdateInPhase(SystemPhase.Simulation)][TickMode(TickPolicy.EveryFrame)]
public sealed class MoveSystem : IEcsModuleSystem
{
    private readonly SimSettings _s;
    private EntityQuery? _q;
    public MoveSystem(SimSettings s) => _s = s;
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (_s.Paused) return;
        var repo = (EntityRepository)view;
        _q ??= repo.Query().With<Position>().With<Velocity>().Build();
        foreach (var e in _q)
        {
            ref var p = ref repo.GetComponentRW<Position>(e);
            ref var v = ref repo.GetComponentRW<Velocity>(e);
            p.X += v.X * deltaTime; p.Y += v.Y * deltaTime;
            bool bounced = false;
            if (p.X < 0 || p.X > 1000) { v.X = -v.X; bounced = true; }
            if (p.Y < 0 || p.Y > 1000) { v.Y = -v.Y; bounced = true; }
            if (bounced) { repo.Bus.Publish(new Bounce { Who = e, X = p.X, Y = p.Y }); Interlocked.Increment(ref _s.BouncesPublished); }
        }
    }
}

/// <summary>The SYNC POINT: at the end of each tick the live world is published as a fresh pooled snapshot.</summary>
[UpdateInPhase(SystemPhase.Export)][TickMode(TickPolicy.EveryFrame)]
public sealed class PublishSystem : IEcsModuleSystem
{
    private readonly SnapshotPort _port;
    public PublishSystem(SnapshotPort port) => _port = port;
    public void Execute(ISimulationView view, float deltaTime) => _port.Publish((EntityRepository)view);
}

/// <summary>Per-tick timing of the worker, readable from the UI.</summary>
public sealed class WorkerStats
{
    public double TickMs, PublishMs;
    public long Ticks;
    public readonly Stopwatch Clock = Stopwatch.StartNew();
}

/// <summary>A plain continuous clock: the caller sets the next delta before each kernel update.</summary>
public sealed class LoopClock : Fdp.ModuleHost.Time.ITimeController
{
    public float NextDelta; public double Total; public long Frame;
    public GlobalTime Update()
    {
        Total += NextDelta; Frame++;
        var g = new GlobalTime { DeltaTime = NextDelta, TotalTime = Total, FrameNumber = Frame, TimeScale = 1f };
        NextDelta = 0f; return g;
    }
    public void SetTimeScale(float scale) { }
    public float GetTimeScale() => 1f;
    public TimeMode GetMode() => TimeMode.Continuous;
    public GlobalTime GetCurrentState() => new() { TotalTime = Total, FrameNumber = Frame };
    public void SeedState(GlobalTime state) { Total = state.TotalTime; Frame = state.FrameNumber; }
    public void Dispose() { }
    public long TimelineEpoch { get; set; }
}

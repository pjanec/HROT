using System.Diagnostics;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.ThreadedKernel;

/// <summary>
/// ⭐ The PRESENTATION's own state — the data-only store of §13 (selection, the drag ghost, the prepared primitives). It is NOT in World A:
/// a snapshot is read-only, and this changes every frame. Keyed by the simulation's Entity (a snapshot keeps World A's handles).
/// </summary>
public sealed class UiStore
{
    // input, written by the render loop before the presentation runs (canvas-local pixels; the canvas maps 1000 × 1000 world units)
    public Vector2 Mouse; public bool MouseInCanvas, Down, Pressed, Released; public float CanvasScale = 0.6f;
    // selection and drag
    public Entity Selected = Entity.Null; public bool Dragging; public Vector2 Ghost;
    // prepared primitives (grown, never shrunk — 0 B per steady frame)
    public float[] Xs = new float[1024], Ys = new float[1024]; public Entity[] Ids = new Entity[1024]; public int Count;
    public Vector2 SelectedPos; public bool SelectedAlive;
}

/// <summary>
/// ⭐ The NEW STRATEGY under test — a THREAD-AFFINE, PULLED module: the render loop (main thread) runs its systems itself, every frame, on the
/// leased snapshot. The kernel never dispatches it. It is an ordinary IEcsModule; its systems are ordinary IEcsModuleSystems.
/// Per-system timings are kept here — in the real feature they would be reported into the kernel's one profiler.
/// </summary>
public sealed class PulledModuleRunner : ISystemRegistry
{
    private readonly List<IEcsModuleSystem> _systems = new();
    public readonly List<(string Name, double Ms)> Timings = new();
    public PulledModuleRunner(IEcsModule module) => module.RegisterSystems(this);
    public void RegisterSystem<T>(T system) where T : IEcsModuleSystem { _systems.Add(system); Timings.Add((typeof(T).Name, 0)); }
    public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem { RegisterSystem(system); return system; }

    public void RunFrame(ISimulationView view, float dt)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            long t = Stopwatch.GetTimestamp();
            _systems[i].Execute(view, dt);
            Timings[i] = (Timings[i].Name, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
    }
}

[TickMode(TickPolicy.EveryFrame)]
public sealed class PresentationModule : IEcsModule
{
    private readonly UiStore _ui; private readonly CommandIngress _out;
    public PresentationModule(UiStore ui, CommandIngress commands) { _ui = ui; _out = commands; }
    public string Name => "Presentation";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();   // ⚠ PoC: the policy the real feature adds ("pulled, thread-affine") does not exist yet
    public void Tick(ISimulationView view, float deltaTime) { }   // all work is in the registered systems
    public void RegisterSystems(ISystemRegistry r)
    {
        r.RegisterSystem(new GizmoPrepSystem(_ui));
        r.RegisterSystem(new InteractionSystem(_ui, _out));
    }
}

/// <summary>"Map preparation": every entity's position from the snapshot into the primitive buffers.</summary>
public sealed class GizmoPrepSystem : IEcsModuleSystem
{
    private readonly UiStore _ui;
    private EntityQuery? _q; private ISimulationView? _qView;
    public GizmoPrepSystem(UiStore ui) => _ui = ui;
    public void Execute(ISimulationView view, float dt)
    {
        if (!ReferenceEquals(view, _qView)) { _q = view.Query().With<Position>().Build(); _qView = view; }   // ⚠ finding: a query is bound to ONE repository — re-built per snapshot
        int n = 0;
        foreach (var e in _q!)
        {
            if (n == _ui.Xs.Length) { Array.Resize(ref _ui.Xs, n * 2); Array.Resize(ref _ui.Ys, n * 2); Array.Resize(ref _ui.Ids, n * 2); }
            ref readonly var p = ref view.GetComponentRO<Position>(e);
            _ui.Xs[n] = p.X; _ui.Ys[n] = p.Y; _ui.Ids[n] = e; n++;
        }
        _ui.Count = n;
        _ui.SelectedAlive = _ui.Selected != Entity.Null && view.IsAlive(_ui.Selected) && view.HasComponent<Position>(_ui.Selected);
        if (_ui.SelectedAlive) { ref readonly var sp = ref view.GetComponentRO<Position>(_ui.Selected); _ui.SelectedPos = new Vector2(sp.X, sp.Y); }
    }
}

/// <summary>Picking and dragging: selection and the ghost live in the UiStore; only the RELEASE becomes a command to World A.</summary>
public sealed class InteractionSystem : IEcsModuleSystem
{
    private readonly UiStore _ui; private readonly CommandIngress _out;
    public InteractionSystem(UiStore ui, CommandIngress o) { _ui = ui; _out = o; }
    public void Execute(ISimulationView view, float dt)
    {
        var world = _ui.Mouse / _ui.CanvasScale;
        if (_ui.Pressed && _ui.MouseInCanvas)
        {
            float best = 12f / _ui.CanvasScale; best *= best; Entity hit = Entity.Null;
            for (int i = 0; i < _ui.Count; i++)
            {
                float dx = _ui.Xs[i] - world.X, dy = _ui.Ys[i] - world.Y, d = dx * dx + dy * dy;
                if (d < best) { best = d; hit = _ui.Ids[i]; }
            }
            _ui.Selected = hit;
            _ui.Dragging = hit != Entity.Null;
        }
        if (_ui.Dragging && _ui.Down) _ui.Ghost = world;
        if (_ui.Dragging && _ui.Released)
        {
            _ui.Dragging = false;
            if (view.IsAlive(_ui.Selected))
            {
                var b = _out.Rent();
                b.SetComponent(_ui.Selected, new Position { X = _ui.Ghost.X, Y = _ui.Ghost.Y });
                b.SetComponent(_ui.Selected, new Velocity());
                _out.Submit(b);
            }
        }
    }
}

using System.Diagnostics;
using Fdp.Toolkit.Orchestration;
using System.Numerics;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Core;

namespace Fdp.Toolkit.Runner
{
    /// <summary>
    /// Manages the full lifecycle of all registered subsystems.
    ///
    /// <para>Lifecycle order per frame:
    /// <list type="number">
    ///   <item>Update all subsystems</item>
    ///   <item>DrawWorld on the active map subsystem (non-headless only)</item>
    ///   <item>DrawUI on all subsystems (non-headless only)</item>
    /// </list>
    /// In headless mode the rendering step is skipped entirely.
    /// </para>
    ///
    /// <para>Project-specific coupling removed (DB-MOD1-08 / MOD1-P9T2):
    /// <list type="bullet">
    ///   <item>No <c>BuildSubsystems</c> factory: subsystems are injected via constructor.</item>
    ///   <item>No hardcoded colour switch: each subsystem exposes <see cref="ISubsystem.TitleBarColor"/>.</item>
    ///   <item>No hardcoded menu buttons: menu items are generated from <see cref="IMapCameraProvider"/> implementors.</item>
    /// </list>
    /// </para>
    ///
    /// <para>Rendering (Raylib window, rlImGui bootstrap) is now owned by the Composition
    /// Root (Program.cs). This orchestrator is pure simulation loop only.</para>
    /// </summary>
    public class SubsystemOrchestrator
    {
        private readonly List<ISubsystem> _subsystems;
        private readonly bool _headless;
        private readonly int _domainId;
        private readonly NodeId _nodeId;
        private readonly bool _deterministic;
        private readonly float _fixedDeltaSeconds;
        private readonly Func<string, int, int>? _nodeIdResolver;
        private readonly Func<string, string?>? _nodeNameResolver;
        private volatile bool _running = true;
        private readonly bool _seedContent;
        private readonly string? _nasRoot;
        private Stopwatch? _frameTimer;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action<SubsystemOrchestrator>>
            _pendingConsoleActions = new();

        /// <summary>
        /// The subsystem that currently "owns" the map view (DrawWorld is called on it).
        /// Defaults to the first <see cref="IMapCameraProvider"/> subsystem, or <c>null</c>
        /// when no subsystem implements the interface.
        /// </summary>
        private ISubsystem? _activeMapOwner;

        // ── Construction ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates an orchestrator with explicitly injected subsystems.
        /// </summary>
        /// <param name="subsystems">The concrete subsystems to manage.</param>
        /// <param name="options">Generic orchestration options. Defaults to headless=false if <c>null</c>.</param>
        public SubsystemOrchestrator(IEnumerable<ISubsystem> subsystems, RunnerOptions? options = null)
        {
            options ??= new RunnerOptions();
            _headless          = options.Headless;
            _domainId          = options.DomainId;
            _nodeId            = options.NodeId;
            _deterministic     = options.Deterministic;
            _fixedDeltaSeconds = options.FixedDeltaSeconds;
            _nodeIdResolver    = options.NodeIdResolver;
            _nodeNameResolver  = options.NodeNameResolver;
            _seedContent       = options.SeedContent;
            _nasRoot           = options.NasRoot;
            _subsystems        = new List<ISubsystem>(subsystems);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises all subsystems. Window creation is handled by the Composition Root.
        /// </summary>
        public void Initialize()
        {
            // ⭐⭐⭐ CE-3340 ③ — THE ONE PLACE THE CONTENT SEED RUNS (R-4097, DESIGN_Content_Layout §2/§5): every host's start passes through here — the
            // cluster runner, the editor inside it, every in-process test harness — so every host seeds the NAS from the repository (a dev start; a
            // production install has no source tree and this does nothing) BEFORE any subsystem reads the NAS in its own Initialize. ⛔ No host calls the
            // seed itself. A host that must not touch the NAS (a unit test with fakes) sets RunnerOptions.SeedContent = false.
            if (_seedContent) ContentSeed.SeedIntoNas(_nasRoot ?? ContentLayout.NasRoot);

            // Default map owner: first subsystem that provides a map camera.
            _activeMapOwner = _subsystems.FirstOrDefault(s => s is IMapCameraProvider);

            foreach (var subsystem in _subsystems)
            {
                var captured = subsystem; // capture loop variable to avoid closure over iterator
                var cfg = new SubsystemConfig
                {
                    DomainId          = _domainId,
                    Headless          = _headless,
                    OwnWindow         = false,
                    SubsystemName     = subsystem.Name,
                    Deterministic     = _deterministic,
                    FixedDeltaSeconds = _fixedDeltaSeconds,
                    NodeId            = (Fdp.Core.NodeId)(_nodeIdResolver != null ? _nodeIdResolver(subsystem.Name, _nodeId) : _nodeId),
                    NodeName          = _nodeNameResolver?.Invoke(subsystem.Name),   // ⭐ CE-3354 ②
                    // GZH-016: inject active-map-owner predicate so subsystems can gate canvas input.
                    IsActiveMapOwner  = () => _activeMapOwner == captured,
                    // A subsystem's own control plane can ask the host to leave the frame loop.
                    RequestAppExit    = Stop,
                };
                subsystem.Initialize(cfg);
            }
        }

        /// <summary>
        /// Runs the frame loop, blocking until <see cref="Stop"/> is called.
        /// </summary>
        /// <param name="beforeFrame">
        /// ⭐ CE-3327 — the host's frame-boundary work, run first each frame (the windowed loop's order): the cluster debug API drains its
        /// main-thread jobs here. 🔴 Measured live: without it a headless node with no editor never answered a main-thread route.
        /// </param>
        public void Run(Action? beforeFrame = null)
        {
            _frameTimer = Stopwatch.StartNew();
            // ⭐ CE-4366 (L-4, docs/DESIGN_Scale_Rig.md §5) — a headless node's loop never waited: one core at 100 % while idle, per node (the
            //   scale rig's three nodes on a 4-core VM starved the terrain load). HROT_HEADLESS_MAX_HZ caps the loop (opt-in; unset = as before).
            long minFrameTicks = 0;
            if (_headless && int.TryParse(Environment.GetEnvironmentVariable(HeadlessMaxHzVariable), out int maxHz) && maxHz > 0)
                minFrameTicks = Stopwatch.Frequency / maxHz;
            while (_running)
            {
                long frameStart = Stopwatch.GetTimestamp();
                beforeFrame?.Invoke();
                DrainConsoleActions();
                float dt = GetDeltaTime();
                Update(dt);

                if (!_headless)
                {
                    DrawWorldAll();
                    DrawUIAll();
                }
                else if (minFrameTicks > 0)
                {
                    long left = minFrameTicks - (Stopwatch.GetTimestamp() - frameStart);
                    if (left > 0) System.Threading.Thread.Sleep(TimeSpan.FromTicks(left * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
                }
            }
        }

        /// <summary>⭐ CE-4366 — the environment variable that caps a headless node's frame loop (frames per second; unset or 0 = no cap).</summary>
        public const string HeadlessMaxHzVariable = "HROT_HEADLESS_MAX_HZ";

        /// <summary>Signals the frame loop to exit gracefully. Safe to call from any thread.</summary>
        public void Stop() => _running = false;

        /// <summary>
        /// False once <see cref="Stop"/> has been called. The Composition Root's render loop owns
        /// its own <c>while</c>, so it polls this each frame to honour the same request that ends
        /// <see cref="Run"/> — one stop signal, both loops.
        /// </summary>
        public bool IsRunning => _running;

        /// <summary>
        /// Thread-safe enqueue for console-dispatched actions. Called by
        /// <see cref="ConsoleCommandService"/> from the background stdin thread.
        /// The main loop drains this queue by calling <see cref="DrainConsoleActions"/> each tick.
        /// </summary>
        public void EnqueueConsoleAction(Action<SubsystemOrchestrator> action)
            => _pendingConsoleActions.Enqueue(action);

        /// <summary>
        /// Drains all pending console actions on the calling thread (must be the main thread).
        /// </summary>
        public void DrainConsoleActions()
        {
            while (_pendingConsoleActions.TryDequeue(out var action))
                action(this);
        }

        /// <summary>
        /// Runs exactly <paramref name="frames"/> update iterations without rendering.
        /// Used by the headless test executor and unit tests.
        /// </summary>
        public void RunFrames(int frames)
        {
            float dt = _deterministic ? _fixedDeltaSeconds : 0f;
            for (int i = 0; i < frames; i++)
                Update(dt);
        }

        /// <summary>Shuts down all subsystems in reverse order.</summary>
        public void Shutdown()
        {
            for (int i = _subsystems.Count - 1; i >= 0; i--)
                _subsystems[i].Shutdown();
        }

        // ── Map-ownership switching ───────────────────────────────────────────

        /// <summary>
        /// Switches the active map owner to the subsystem whose <see cref="ISubsystem.Name"/>
        /// matches <paramref name="subsystemName"/> and synchronises camera state between the
        /// outgoing and incoming map views.
        /// </summary>
        public void SwitchMapOwner(string subsystemName)
        {
            var target = _subsystems.FirstOrDefault(s => s.Name == subsystemName);
            if (target == null || target == _activeMapOwner) return;

            var outgoing = _activeMapOwner;
            _activeMapOwner = target;

            // Sync cameras so the operator sees the same region without any jump.
            if (outgoing is IMapCameraProvider fromProvider && target is IMapCameraProvider toProvider)
            {
                MapCameraView? fromView = fromProvider.GetCameraView();
                if (fromView != null)
                    toProvider.ApplyCameraView(fromView.Value);
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private float GetDeltaTime()
        {
            if (_deterministic) return _fixedDeltaSeconds;
            if (_headless) return 0f;
            // Non-headless, non-deterministic: use wall clock.
            float dt = (float)(_frameTimer?.Elapsed.TotalSeconds ?? 0.0);
            _frameTimer?.Restart();
            return dt;
        }

        // ════ ⭐⭐ N-13 (CE-4475; U-2 / F-21) — ONE sim worker for every kernel of the process ════════════════════════════════════════════
        //   A windowed threaded process (HROT_THREADED_KERNEL=3) hosting several kernels runs them ALL on one worker thread, in sequence: the
        //   host's sim-side frame-boundary work (its debug API's sim-side routes), then each subsystem's sim step — a staged subsystem's own
        //   (ISimSteppedSubsystem), a plain subsystem's Update. The main thread runs only the staged subsystems' presentation. ⭐ One thread for
        //   every kernel ⇒ the process-wide statics (the worker pool, the per-job runners, the timers) are never entered from two threads.
        //   ⚠ A subsystem with no presentation split is not DRAWN while its kernel runs on the worker (it would read its world beside the tick);
        //   only a staged subsystem presents. Headless processes keep everything inline (nothing is drawn: no race to prevent).

        private System.Threading.Thread? _simWorker;
        private Action? _simBoundary;
        private readonly object _simDtLock = new();
        private double _simPendingDt;
        private long _simWorkerFaults;

        /// <summary>The kernels run on the host's one sim worker (<see cref="StartSimWorker"/>).</summary>
        public bool SimOnWorker => _simWorker != null;

        /// <summary>⭐ N-13 — may <paramref name="subsystem"/>'s presentation run on the main thread now? Always without a sim worker; beside it only a
        /// staged subsystem (<see cref="ISimSteppedSubsystem"/> — the UI reads its published copy), never one whose live world the worker ticks.
        /// ⭐ The ONE rule: this orchestrator's draw passes and the window gate the runner gives each subsystem's windows (WindowManager.RegisteringUnder).</summary>
        public bool Draws(ISubsystem subsystem) => _simWorker == null || subsystem is ISimSteppedSubsystem;

        /// <summary>Faults the sim worker caught (each is also logged, the first 20).</summary>
        public long SimWorkerFaults => System.Threading.Interlocked.Read(ref _simWorkerFaults);

        /// <summary>
        /// ⭐ N-13 — starts the ONE sim worker of this process. <paramref name="simBoundary"/> is the host's sim-side frame-boundary work, run on the
        /// worker before each pass (the debug API's sim-side routes). Call once, after <see cref="Initialize"/>.
        /// </summary>
        public void StartSimWorker(Action? simBoundary)
        {
            if (_simWorker != null) return;
            _simBoundary = simBoundary;
            for (int i = 0; i < _subsystems.Count; i++)
                if (_subsystems[i] is ISimSteppedSubsystem staged) staged.UseHostSimWorker();
            _simWorker = new System.Threading.Thread(SimWorkerLoop) { IsBackground = true, Name = "HROT sim (all kernels)" };
            _simWorker.Start();
        }

        private void SimWorkerLoop()
        {
            Fdp.Core.Threading.WindowsScheduling.OptOutCurrentThread();   // ⭐ N-14 — the sim thread
            while (_running)
            {
                bool did = false;
                try
                {
                    _simBoundary?.Invoke();
                    float dt;
                    lock (_simDtLock)
                    {
                        dt = (float)Math.Min(_simPendingDt, 0.25);
                        _simPendingDt = _simPendingDt - dt > 0.25 ? 0 : _simPendingDt - dt;   // never more than a quarter second behind
                    }
                    for (int i = 0; i < _subsystems.Count; i++)
                    {
                        if (_subsystems[i] is ISimSteppedSubsystem staged) did |= staged.SimStepOnHostWorker();
                        else if (dt > 0f)
                        {
                            using var trace = Fdp.Core.AccessTrace.Scope(_subsystems[i], "subsystem.Update (sim worker): ", Fdp.Core.TraceContext.Tick);
                            _subsystems[i].Update(dt);
                            did = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (System.Threading.Interlocked.Increment(ref _simWorkerFaults) <= 20) Console.WriteLine("[N-13] sim worker fault: " + ex);
                }
                if (!did) System.Threading.Thread.Sleep(1);
            }
        }

        public void Update(float dt)
        {
            if (_simWorker != null)
            {   // ⭐ N-13 — the main thread: the staged subsystems' presentation only; the plain kernels' time goes to the worker
                lock (_simDtLock) _simPendingDt += dt;
                for (int i = 0; i < _subsystems.Count; i++)
                    if (_subsystems[i] is ISimSteppedSubsystem)
                    {
                        using var traceStaged = Fdp.Core.AccessTrace.Scope(_subsystems[i], "subsystem.Update: ", Fdp.Core.TraceContext.Presentation);
                        _subsystems[i].Update(dt);
                    }
                return;
            }
            for (int i = 0; i < _subsystems.Count; i++)
            {
                using var traceSub = Fdp.Core.AccessTrace.Scope(_subsystems[i], "subsystem.Update: ", Fdp.Core.TraceContext.Presentation);   // ⭐ A-1 — the presentation half of the frame, one reader per subsystem
                _subsystems[i].Update(dt);
            }
        }

        public void DrawWorldAll()
        {
            // Only the active map owner draws the world layer.
            for (int i = 0; i < _subsystems.Count; i++)
            {
                if (IsMapOwner(_subsystems[i]) && Draws(_subsystems[i]))   // ⭐ N-13 — only a staged subsystem draws beside the worker
                {
                    using var traceSub = Fdp.Core.AccessTrace.Scope(_subsystems[i], "subsystem.DrawWorld: ", Fdp.Core.TraceContext.Presentation);   // ⭐ A-1
                    _subsystems[i].DrawWorld();
                }
            }
        }

        public void DrawUIAll()
        {
            for (int i = 0; i < _subsystems.Count; i++)
            {
                if (!Draws(_subsystems[i])) continue;   // ⭐ N-13 — its world is ticking on the worker
                using var traceSub = Fdp.Core.AccessTrace.Scope(_subsystems[i], "subsystem.DrawUI: ", Fdp.Core.TraceContext.Presentation);   // ⭐ A-1
                _subsystems[i].DrawUI();
            }
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="subsystem"/> should draw its world layer.
        /// Non-map subsystems always draw; map subsystems only draw when they are the active owner.
        /// </summary>
        private bool IsMapOwner(ISubsystem subsystem)
            => !(subsystem is IMapCameraProvider)   // non-map always draws
               || subsystem == _activeMapOwner;
    }
}

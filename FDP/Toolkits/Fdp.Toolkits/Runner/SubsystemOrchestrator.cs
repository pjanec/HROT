using System.Diagnostics;
using System.Numerics;
using Fdp.Toolkit.Vis2D.Components;

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
        private readonly int _nodeId;
        private readonly bool _deterministic;
        private readonly float _fixedDeltaSeconds;
        private readonly Func<string, int, int>? _nodeIdResolver;
        private volatile bool _running = true;
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
            _subsystems        = new List<ISubsystem>(subsystems);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises all subsystems. Window creation is handled by the Composition Root.
        /// </summary>
        public void Initialize()
        {
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
                    NodeId            = _nodeIdResolver != null ? _nodeIdResolver(subsystem.Name, _nodeId) : _nodeId,
                    // GZH-016: inject active-map-owner predicate so subsystems can gate canvas input.
                    IsActiveMapOwner  = () => _activeMapOwner == captured,
                };
                subsystem.Initialize(cfg);
            }
        }

        /// <summary>
        /// Runs the frame loop, blocking until <see cref="Stop"/> is called.
        /// </summary>
        public void Run()
        {
            _frameTimer = Stopwatch.StartNew();
            while (_running)
            {
                DrainConsoleActions();
                float dt = GetDeltaTime();
                Update(dt);

                if (!_headless)
                {
                    DrawWorldAll();
                    DrawUIAll();
                }
            }
        }

        /// <summary>Signals the frame loop to exit gracefully.</summary>
        public void Stop() => _running = false;

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

        public void Update(float dt)
        {
            for (int i = 0; i < _subsystems.Count; i++)
                _subsystems[i].Update(dt);
        }

        public void DrawWorldAll()
        {
            // Only the active map owner draws the world layer.
            for (int i = 0; i < _subsystems.Count; i++)
            {
                if (IsMapOwner(_subsystems[i]))
                    _subsystems[i].DrawWorld();
            }
        }

        public void DrawUIAll()
        {
            for (int i = 0; i < _subsystems.Count; i++)
                _subsystems[i].DrawUI();
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

using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using System.Numerics;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.ImGui.Icons;
using WM = FDP.Toolkit.ImGui.WindowManager.WindowManager;

namespace FDP.Framework.Runner
{
    /// <summary>
    /// Manages the full lifecycle of all registered subsystems and owns the
    /// Raylib window + rlImGui context in non-headless mode.
    ///
    /// <para>Lifecycle order per frame:
    /// <list type="number">
    ///   <item>Update all subsystems</item>
    ///   <item>BeginDrawing → DrawWorld on the active map subsystem → rlImGui.Begin → DrawUI on all → rlImGui.End → EndDrawing</item>
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
    /// </summary>
    public class SubsystemOrchestrator
    {
        private const string WindowTitle = "FDP Runner";

        private readonly List<ISubsystem> _subsystems;
        private readonly bool _headless;
        private readonly int _domainId;
        private readonly int _nodeId;
        private readonly int _windowWidth;
        private readonly int _windowHeight;
        private readonly int _targetFps;
        private readonly bool _deterministic;
        private readonly float _fixedDeltaSeconds;
        private readonly Func<string, int, int>? _nodeIdResolver;
        private volatile bool _running = true;

        /// <summary>
        /// The subsystem that currently "owns" the map view (DrawWorld is called on it).
        /// Defaults to the first <see cref="IMapCameraProvider"/> subsystem, or <c>null</c>
        /// when no subsystem implements the interface.
        /// </summary>
        private ISubsystem? _activeMapOwner;

        /// <summary>
        /// The Window Manager that owns the menu bar and all registered panels.
        /// <c>null</c> in headless mode.
        /// </summary>
        private WM? _windowManager;

        /// <summary>Dummy icon atlas held alongside <see cref="_windowManager"/>; disposed on Shutdown.</summary>
        private IconAtlas? _iconAtlas;

        /// <summary>
        /// The active Window Manager, or <c>null</c> when running headless.
        /// Subsystems that registered windows during <c>Initialize</c> can access this
        /// to manipulate panels programmatically after startup.
        /// </summary>
        public WM? WindowManager => _windowManager;

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
            _windowWidth       = options.WindowWidth;
            _windowHeight      = options.WindowHeight;
            _targetFps         = options.TargetFps;
            _deterministic     = options.Deterministic;
            _fixedDeltaSeconds = options.FixedDeltaSeconds;
            _nodeIdResolver    = options.NodeIdResolver;
            _subsystems        = new List<ISubsystem>(subsystems);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the Raylib window (non-headless) and initialises all subsystems.
        /// </summary>
        public void Initialize()
        {
            if (!_headless)
            {
                Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
                Raylib.InitWindow(_windowWidth, _windowHeight, WindowTitle);
                Raylib.SetTargetFPS(_targetFps);
                rlImGui.Setup(true);

                // WM-S402: Enable ImGui docking.
                ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;

                // WM-S501: Create the WindowManager with a dummy atlas (no GPU texture;
                // icons render as blank squares — acceptable until production atlas is wired in).
                _iconAtlas    = new IconAtlas(IntPtr.Zero, 256f, 256f, 16f);
                _windowManager = new WM(_iconAtlas);
            }

            // Default map owner: first subsystem that provides a map camera.
            _activeMapOwner = _subsystems.FirstOrDefault(s => s is IMapCameraProvider);

            foreach (var subsystem in _subsystems)
            {
                var cfg = new SubsystemConfig
                {
                    DomainId          = _domainId,
                    Headless          = _headless,
                    OwnWindow         = false,
                    SubsystemName     = subsystem.Name,
                    Deterministic     = _deterministic,
                    FixedDeltaSeconds = _fixedDeltaSeconds,
                    NodeId            = _nodeIdResolver != null ? _nodeIdResolver(subsystem.Name, _nodeId) : _nodeId,
                };
                subsystem.Initialize(cfg);

                // IWindowRegistrar: subsystems that need to register panels call this
                // after Initialize so they can use any state set up during Initialize.
                if (_windowManager != null && subsystem is IWindowRegistrar registrar)
                    registrar.RegisterWindows(_windowManager);
            }
        }

        /// <summary>
        /// Runs the frame loop, blocking until window close or <see cref="Stop"/> is called.
        /// </summary>
        public void Run()
        {
            while (_running && (_headless || !Raylib.WindowShouldClose()))
            {
                float dt = _headless
                    ? (_deterministic ? _fixedDeltaSeconds : 0f)
                    : (_deterministic ? _fixedDeltaSeconds : Raylib.GetFrameTime());
                Update(dt);

                if (!_headless)
                    Render();
            }
        }

        /// <summary>Signals the frame loop to exit gracefully.</summary>
        public void Stop() => _running = false;

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

        /// <summary>Shuts down all subsystems in reverse order and closes the window.</summary>
        public void Shutdown()
        {
            for (int i = _subsystems.Count - 1; i >= 0; i--)
                _subsystems[i].Shutdown();

            if (!_headless)
            {
                rlImGui.Shutdown();
                _iconAtlas?.Dispose();
                Raylib.CloseWindow();
            }
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
                MapCamera? fromCamera = fromProvider.GetMapCamera();
                MapCamera? toCamera   = toProvider.GetMapCamera();
                if (fromCamera != null && toCamera != null)
                    toCamera.SnapTo(fromCamera);
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void Update(float dt)
        {
            for (int i = 0; i < _subsystems.Count; i++)
                _subsystems[i].Update(dt);
        }

        private void Render()
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            // Only the active map owner draws the world layer.
            for (int i = 0; i < _subsystems.Count; i++)
            {
                if (IsMapOwner(_subsystems[i]))
                    _subsystems[i].DrawWorld();
            }

            rlImGui.Begin();

            // WM-S402: Create a fullscreen transparent dockspace that passes mouse
            // events to the underlying Raylib map render.  Must appear before any
            // managed window or subsystem UI to avoid Z-order issues.
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(viewport.WorkSize);
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding,  0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
            var dockspaceFlags = ImGuiWindowFlags.NoDocking
                | ImGuiWindowFlags.NoTitleBar  | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoResize    | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus
                | ImGuiWindowFlags.NoBackground;
            ImGui.Begin("##DockSpace", dockspaceFlags);
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);

            // WM-S503: Reduce dockspace height to leave room for the status bar.
            // StatusBar.Height is computed each frame during WindowManager.Render().
            float statusBarHeight = _windowManager?.StatusBar.Height ?? 0f;
            var dockspaceSize = statusBarHeight > 0f
                ? new Vector2(viewport.WorkSize.X, viewport.WorkSize.Y - statusBarHeight)
                : Vector2.Zero;
            ImGui.DockSpace(ImGui.GetID("MainDockSpace"), dockspaceSize, ImGuiDockNodeFlags.PassthruCentralNode);
            ImGui.End();

            // WM-S501: Window Manager renders the global menu bar and all registered
            // managed windows.  Replaces the old DrawMainMenuBar() private method.
            _windowManager?.Render();

            for (int i = 0; i < _subsystems.Count; i++)
            {
                var subsystem = _subsystems[i];
                // Apply the subsystem's own theme across TitleBg and TitleBgActive.
                Vector4 titleBg       = subsystem.TitleBarColor;
                Vector4 titleBgActive = new Vector4(
                    Math.Min(titleBg.X * 1.4f, 1f),
                    Math.Min(titleBg.Y * 1.4f, 1f),
                    Math.Min(titleBg.Z * 1.4f, 1f),
                    titleBg.W);
                ImGui.PushStyleColor(ImGuiCol.TitleBg,       titleBg);
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, titleBgActive);

                subsystem.DrawUI();

                ImGui.PopStyleColor(2);
            }

            rlImGui.End();
            Raylib.EndDrawing();
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

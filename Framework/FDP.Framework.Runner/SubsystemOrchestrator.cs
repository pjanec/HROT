using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using System.Numerics;
using FDP.Toolkit.Vis2D.Components;

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
        private readonly int _windowWidth;
        private readonly int _windowHeight;
        private readonly int _targetFps;
        private volatile bool _running = true;

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
            _headless     = options.Headless;
            _domainId     = options.DomainId;
            _windowWidth  = options.WindowWidth;
            _windowHeight = options.WindowHeight;
            _targetFps    = options.TargetFps;
            _subsystems   = new List<ISubsystem>(subsystems);
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
            }

            // Default map owner: first subsystem that provides a map camera.
            _activeMapOwner = _subsystems.FirstOrDefault(s => s is IMapCameraProvider);

            foreach (var subsystem in _subsystems)
            {
                var cfg = new SubsystemConfig
                {
                    DomainId      = _domainId,
                    Headless      = _headless,
                    OwnWindow     = false,
                    SubsystemName = subsystem.Name
                };
                subsystem.Initialize(cfg);
            }
        }

        /// <summary>
        /// Runs the frame loop, blocking until window close or <see cref="Stop"/> is called.
        /// </summary>
        public void Run()
        {
            while (_running && (_headless || !Raylib.WindowShouldClose()))
            {
                float dt = _headless ? 0f : Raylib.GetFrameTime();
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
            for (int i = 0; i < frames; i++)
                Update(0f);
        }

        /// <summary>Shuts down all subsystems in reverse order and closes the window.</summary>
        public void Shutdown()
        {
            for (int i = _subsystems.Count - 1; i >= 0; i--)
                _subsystems[i].Shutdown();

            if (!_headless)
            {
                rlImGui.Shutdown();
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
            DrawMainMenuBar();

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

        private void DrawMainMenuBar()
        {
            if (!ImGui.BeginMainMenuBar()) return;

            // Generate a toggle button for every subsystem that exposes a map view.
            var mapProviders = _subsystems.Where(s => s is IMapCameraProvider).ToList();
            if (mapProviders.Count > 0)
            {
                ImGui.Text("Map:");
                foreach (var sub in mapProviders)
                {
                    ImGui.SameLine();
                    bool isActive = sub == _activeMapOwner;
                    if (isActive)
                        ImGui.PushStyleColor(ImGuiCol.Button, sub.TitleBarColor);

                    if (ImGui.Button(sub.Name))
                        SwitchMapOwner(sub.Name);

                    if (isActive)
                        ImGui.PopStyleColor();
                }
            }

            ImGui.EndMainMenuBar();
        }
    }
}

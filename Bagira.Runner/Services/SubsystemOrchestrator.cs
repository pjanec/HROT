using Raylib_cs;
using rlImGui_cs;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Configuration;
using Bagira.Runner.Models;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// Manages the full lifecycle of all registered subsystems and owns the
    /// Raylib window + rlImGui context in aggregated (non-headless) mode.
    ///
    /// <para>Lifecycle order per frame:
    /// <list type="number">
    ///   <item>Update all subsystems</item>
    ///   <item>BeginDrawing → DrawWorld on all subsystems → rlImGui.Begin → DrawUI on all → rlImGui.End → EndDrawing</item>
    /// </list>
    /// In headless mode the rendering step is skipped entirely.
    /// </para>
    /// </summary>
    public class SubsystemOrchestrator
    {
        // ── Window configuration ──────────────────────────────────────────────
        private const int DefaultWindowWidth  = 1600;
        private const int DefaultWindowHeight = 900;
        private const int DefaultTargetFps    = 60;
        private const string WindowTitle      = "Bagira Runner";

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<ISubsystem> _subsystems;
        private readonly bool _headless;
        private readonly int _windowWidth;
        private readonly int _windowHeight;
        private volatile bool _running = true;

        // ── Construction ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates an orchestrator that builds its subsystem list from
        /// <paramref name="config"/>.<see cref="RunnerConfiguration.ParsedMode"/>.
        /// </summary>
        public SubsystemOrchestrator(RunnerConfiguration config)
            : this(config, BuildSubsystems(config))
        {
        }

        /// <summary>
        /// Creates an orchestrator with an explicit subsystem list.
        /// Used in unit tests to inject mock subsystems.
        /// </summary>
        public SubsystemOrchestrator(RunnerConfiguration config, IEnumerable<ISubsystem> subsystems)
        {
            _headless     = config.Headless;
            _windowWidth  = DefaultWindowWidth;
            _windowHeight = DefaultWindowHeight;
            _subsystems   = new List<ISubsystem>(subsystems);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises the Raylib window (when not headless) then initialises all subsystems.
        /// </summary>
        public void Initialize()
        {
            if (!_headless)
            {
                Raylib.InitWindow(_windowWidth, _windowHeight, WindowTitle);
                Raylib.SetTargetFPS(DefaultTargetFps);
                rlImGui.Setup(true);
            }

            bool hasIg = _subsystems.Exists(subsystem => subsystem.Name == "IG");

            foreach (var subsystem in _subsystems)
            {
                var cfg = new SubsystemConfig
                {
                    Headless       = _headless || (hasIg && subsystem.Name == "SimHost"),
                    OwnWindow      = false, // Orchestrator owns window
                    SubsystemName  = subsystem.Name
                };
                subsystem.Initialize(cfg);
            }
        }

        /// <summary>
        /// Runs the main loop until the window is closed (or <see cref="Stop"/> is called).
        /// Blocks the calling thread.
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

        /// <summary>Signals the render loop to stop on the next iteration.</summary>
        public void Stop() => _running = false;

        /// <summary>
        /// Runs exactly <paramref name="frames"/> update iterations (no rendering).
        /// For use in unit tests only — always runs headless regardless of config.
        /// </summary>
        internal void RunFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
                Update(0f);
        }

        /// <summary>
        /// Shuts down all subsystems in reverse registration order then releases
        /// the Raylib window (when not headless).
        /// </summary>
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

            // World layer (3-D content, camera transforms)
            for (int i = 0; i < _subsystems.Count; i++)
                _subsystems[i].DrawWorld();

            // UI layer (ImGui)
            rlImGui.Begin();
            for (int i = 0; i < _subsystems.Count; i++)
                _subsystems[i].DrawUI();
            rlImGui.End();

            Raylib.EndDrawing();
        }

        private static IEnumerable<ISubsystem> BuildSubsystems(RunnerConfiguration config)
        {
            var list = new List<ISubsystem>();
            if (config.ParsedMode.HasFlag(RunMode.SimHost)) list.Add(new SimHostSubsystem());
            if (config.ParsedMode.HasFlag(RunMode.IG))      list.Add(new IgSubsystem());
            if (config.ParsedMode.HasFlag(RunMode.IOS))     list.Add(new IosSubsystem());
            return list;
        }
    }
}

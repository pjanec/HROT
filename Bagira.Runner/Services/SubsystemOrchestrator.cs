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
        private readonly int _domainId;
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
            _domainId     = config.DomainId;
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
                    DomainId       = _domainId,
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

            // UI layer (ImGui) — each subsystem's windows use a distinct title-bar
            // colour so operators can tell at a glance which subsystem owns each panel.
            rlImGui.Begin();
            for (int i = 0; i < _subsystems.Count; i++)
            {
                int pushed = PushSubsystemColors(_subsystems[i].Name);
                _subsystems[i].DrawUI();
                ImGuiNET.ImGui.PopStyleColor(pushed);
            }
            rlImGui.End();

            Raylib.EndDrawing();
        }

        /// <summary>
        /// Pushes ImGui title-bar colour overrides for the named subsystem and returns
        /// the number of colour slots pushed (to pass back to <c>ImGui.PopStyleColor</c>).
        /// Subsystems without a designated colour push nothing and return 0.
        /// IG windows are tinted blue; IOS windows are tinted violet.
        /// </summary>
        private static int PushSubsystemColors(string subsystemName)
        {
            System.Numerics.Vector4 titleBg, titleBgActive;
            switch (subsystemName)
            {
                case "IG":
                    titleBg       = new System.Numerics.Vector4(0.10f, 0.30f, 0.70f, 0.80f);
                    titleBgActive = new System.Numerics.Vector4(0.15f, 0.40f, 0.85f, 0.95f);
                    break;
                case "IOS":
                    titleBg       = new System.Numerics.Vector4(0.45f, 0.10f, 0.70f, 0.80f);
                    titleBgActive = new System.Numerics.Vector4(0.55f, 0.15f, 0.85f, 0.95f);
                    break;
                default:
                    return 0;
            }
            ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.TitleBg,       titleBg);
            ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.TitleBgActive, titleBgActive);
            return 2;
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

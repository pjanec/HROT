using Raylib_cs;
using rlImGui_cs;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Configuration;
using Bagira.Runner.Models;
using FDP.Toolkit.Vis2D.Components;

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

        /// <summary>
        /// Name of the subsystem whose map toolkit (DrawWorld) is currently active.
        /// Toggled at runtime via the main menu bar.  Defaults to "IG" when IG is
        /// present, otherwise "SimHost".
        /// </summary>
        private string _activeMapOwner = "";

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
                Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
                Raylib.InitWindow(_windowWidth, _windowHeight, WindowTitle);
                Raylib.SetTargetFPS(DefaultTargetFps);
                rlImGui.Setup(true);
            }

            bool hasIg      = _subsystems.Exists(s => s.Name == "IG");
            bool hasSimHost  = _subsystems.Exists(s => s.Name == "SimHost");
            _activeMapOwner  = hasIg ? "IG" : (hasSimHost ? "SimHost" : "");

            foreach (var subsystem in _subsystems)
            {
                var cfg = new SubsystemConfig
                {
                    DomainId       = _domainId,
                    Headless       = _headless,   // orchestrator no longer forces SimHost headless
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

            // World layer — only the active map owner renders its canvas.
            for (int i = 0; i < _subsystems.Count; i++)
            {
                if (IsMapOwner(_subsystems[i].Name))
                    _subsystems[i].DrawWorld();
            }

            // UI layer (ImGui) — each subsystem's windows use a distinct title-bar
            // colour so operators can tell at a glance which subsystem owns each panel.
            rlImGui.Begin();
            DrawMainMenuBar();
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
        /// Returns true when the named subsystem is the active map owner (may
        /// call DrawWorld), or when the subsystem is not a map-capable subsystem
        /// (IOS, etc.) and should always render.
        /// </summary>
        private bool IsMapOwner(string name)
            => string.IsNullOrEmpty(_activeMapOwner)
               || _activeMapOwner == name
               || (name != "IG" && name != "SimHost");

        /// <summary>
        /// Draws the Runner main menu bar with a dual-state IG / SimHost map-owner
        /// toggle.  The active button is highlighted with its subsystem colour.
        /// </summary>
        private void DrawMainMenuBar()
        {
            if (!ImGuiNET.ImGui.BeginMainMenuBar()) return;

            ImGuiNET.ImGui.Text("Map:");
            ImGuiNET.ImGui.SameLine();

            bool igOwner = _activeMapOwner == "IG";
            bool shOwner = _activeMapOwner == "SimHost";

            // IG button — highlight green when active
            if (igOwner)
                ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.Button,
                    new System.Numerics.Vector4(0.12f, 0.56f, 0.12f, 1f));
            if (ImGuiNET.ImGui.Button("IG"))
                SwitchMapOwner("IG");
            if (igOwner)
                ImGuiNET.ImGui.PopStyleColor();

            ImGuiNET.ImGui.SameLine();

            // SimHost button — highlight red when active
            if (shOwner)
                ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.Button,
                    new System.Numerics.Vector4(0.56f, 0.12f, 0.12f, 1f));
            if (ImGuiNET.ImGui.Button("SimHost"))
                SwitchMapOwner("SimHost");
            if (shOwner)
                ImGuiNET.ImGui.PopStyleColor();

            ImGuiNET.ImGui.EndMainMenuBar();
        }

        /// <summary>
        /// Switches the active map owner to <paramref name="newOwner"/> and synchronises
        /// the incoming subsystem's map camera to the outgoing one so that entities do not
        /// jump position when the operator toggles between IG and SimHost perspectives.
        /// No-op when <paramref name="newOwner"/> is already the active owner.
        /// </summary>
        /// <remarks>
        /// Exposed as <c>internal</c> so that headless unit tests can drive perspective
        /// switches without a live ImGui frame.
        /// </remarks>
        internal void SwitchMapOwner(string newOwner)
        {
            if (newOwner == _activeMapOwner)
                return;

            string outgoing = _activeMapOwner;
            _activeMapOwner = newOwner;

            // Synchronise cameras so the incoming view snaps to the same world region.
            MapCamera? fromCamera = FindMapCamera(outgoing);
            MapCamera? toCamera   = FindMapCamera(newOwner);
            if (fromCamera != null && toCamera != null)
                toCamera.SnapTo(fromCamera);
        }

        /// <summary>
        /// Locates the map camera belonging to the named subsystem by checking
        /// whether it implements <see cref="IMapCameraProvider"/>.
        /// </summary>
        private MapCamera? FindMapCamera(string subsystemName)
        {
            foreach (var sub in _subsystems)
                if (sub.Name == subsystemName && sub is IMapCameraProvider provider)
                    return provider.GetMapCamera();
            return null;
        }

        /// <summary>
        /// Pushes ImGui title-bar colour overrides for the named subsystem and returns
        /// the number of colour slots pushed (to pass back to <c>ImGui.PopStyleColor</c>).
        /// Subsystems without a designated colour push nothing and return 0.
        /// IG windows are tinted green, SimHost red, IOS violet.
        /// Per-panel <c>PushStyleColor</c> calls inside each subsystem override these
        /// with their own matching shade for focused / active states.
        /// </summary>
        private static int PushSubsystemColors(string subsystemName)
        {
            System.Numerics.Vector4 titleBg, titleBgActive;
            switch (subsystemName)
            {
                case "IG":
                    titleBg       = new System.Numerics.Vector4(0.08f, 0.40f, 0.08f, 1f);
                    titleBgActive = new System.Numerics.Vector4(0.12f, 0.56f, 0.12f, 1f);
                    break;
                case "SimHost":
                    titleBg       = new System.Numerics.Vector4(0.40f, 0.08f, 0.08f, 1f);
                    titleBgActive = new System.Numerics.Vector4(0.56f, 0.12f, 0.12f, 1f);
                    break;
                case "IOS":
                    titleBg       = new System.Numerics.Vector4(0.32f, 0.08f, 0.48f, 1f);
                    titleBgActive = new System.Numerics.Vector4(0.44f, 0.12f, 0.62f, 1f);
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

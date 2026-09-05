using System;
using ImGuiNET;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost;
using Hrot.UI.Common.Facades;

namespace Hrot.SimHost.UI
{
    // ─── Shared UI state ─────────────────────────────────────────────────────────

    /// <summary>Mutable view-state shared between the spawn panel and the map tool.</summary>
    public class SimHostUIState
    {
        public VehicleClass          SelectedVehicleClass   { get; set; } = VehicleClass.PersonalCar;
        public FormationType         SelectedFormationType  { get; set; } = FormationType.Column;
        public TrajectoryInterpolation InterpolationMode    { get; set; } = TrajectoryInterpolation.CatmullRom;
    }

    // ─── Simulation control panel ─────────────────────────────────────────────────

    /// <summary>
    /// Play / Pause / Step / Time-Scale controls.
    ///
    /// <para><b>`T5`: these controls used to be INERT.</b> Pause toggled a private field, Step set a
    /// private flag, and <c>ConsumeStepRequest</c> had no caller anywhere in the repo — so pressing
    /// Pause on this panel did nothing, silently. The same shape as `AS-9`'s no-op tracer coordinator:
    /// a control that looks wired because an identically-shaped one elsewhere (the CarKinem example's
    /// <c>MainUI</c>) genuinely is.</para>
    ///
    /// <para>SimHost is a SLAVE node, so it must never pause itself — a pause is cluster-wide, issued
    /// as an intent. It already builds a <see cref="ITimeTransportFacade"/> for its status bar
    /// (<c>SimHostSubsystem:263</c>), which is exactly the seam these controls needed, so they are
    /// routed onto it rather than given a control path of their own.</para>
    ///
    /// <para>With no facade the controls render DISABLED. Deliberately: a visibly dead button is a
    /// bug report, and a silently dead one is what this fixed.</para>
    /// </summary>
    public class SimHostSimulationControlsPanel
    {
        /// <summary>
        /// The node's cluster transport, supplied by <c>SimHostSubsystem</c> once the orchestration
        /// bus exists. Null in a host with no bus — see the class remarks for what that renders.
        /// </summary>
        public ITimeTransportFacade? TimeFacade { get; set; }

        /// <summary>Whether the cluster reports itself paused. Answers false with no transport.</summary>
        public bool  IsPaused  => TimeFacade?.IsPaused  ?? false;

        /// <summary>The cluster's time scale. Answers 1 with no transport.</summary>
        public float TimeScale => TimeFacade?.TimeScale ?? 1.0f;

        public void Render(EntityRepository repo, ModuleHostKernel kernel)
        {
            var facade = TimeFacade;
            if (facade == null) ImGui.BeginDisabled();

            if (ImGui.Button(IsPaused ? "Play  " : "Pause ")) facade?.TogglePlayPause();
            ImGui.SameLine();

            if (ImGui.Button("Step")) facade?.Step();
            ImGui.SameLine();

            ImGui.SetNextItemWidth(100);
            float ts = TimeScale;
            if (ImGui.SliderFloat("Speed", ref ts, 0.1f, 5.0f)) facade?.SetTimeScale(ts);

            if (facade == null)
            {
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextDisabled("(no cluster transport)");
            }

            if (repo != null)
            {
                int living = 0;
                var q = repo.Query().With<VehicleState>().Build();
                foreach (var _ in q) living++;
                ImGui.Text($"Entities: {living}");
            }
        }
    }

    // ─── Spawn controls panel ─────────────────────────────────────────────────────

    /// <summary>Vehicle-type / count / formation spawn controls.</summary>
    public class SimHostSpawnPanel
    {
        private int  _count         = 10;
        private bool _randomMovement = true;

        public void Render(SimHostScenarioManager scenario, SimHostUIState uiState)
        {
            ImGui.SliderInt("Count", ref _count, 1, 100);
            ImGui.Checkbox("Random movement", ref _randomMovement);

            // Interpolation Radio
            int iMode = (int)uiState.InterpolationMode;
            ImGui.Text("Path interpolation:");
            if (ImGui.RadioButton("Linear",      ref iMode, 0)) uiState.InterpolationMode = TrajectoryInterpolation.Linear;
            ImGui.SameLine();
            if (ImGui.RadioButton("Catmull-Rom", ref iMode, 1)) uiState.InterpolationMode = TrajectoryInterpolation.CatmullRom;

            // Vehicle class combo
            string[] classNames = Enum.GetNames(typeof(VehicleClass));
            int selIdx = (int)uiState.SelectedVehicleClass;
            if (ImGui.Combo("Vehicle type", ref selIdx, classNames, classNames.Length))
                uiState.SelectedVehicleClass = (VehicleClass)selIdx;

            // Spawn buttons
            if (ImGui.Button("Spawn"))
            {
                if (_randomMovement)
                    scenario.SpawnRoamers(_count, uiState.SelectedVehicleClass, uiState.InterpolationMode);
                else
                    scenario.SpawnRoadUsers(_count, uiState.SelectedVehicleClass);
            }
            ImGui.SameLine();
            if (ImGui.Button("Road users"))  scenario.SpawnRoadUsers(_count, uiState.SelectedVehicleClass);
            ImGui.SameLine();
            if (ImGui.Button("Collision test")) scenario.SpawnCollisionTest(uiState.SelectedVehicleClass);
            ImGui.SameLine();
            if (ImGui.Button("Clear all"))   scenario.ClearAll();

            ImGui.Separator();
            ImGui.Text("Formation:");

            int fType = (int)uiState.SelectedFormationType;
            if (ImGui.RadioButton("Column", ref fType, 0)) uiState.SelectedFormationType = FormationType.Column;
            ImGui.SameLine();
            if (ImGui.RadioButton("Wedge",  ref fType, 1)) uiState.SelectedFormationType = FormationType.Wedge;
            ImGui.SameLine();
            if (ImGui.RadioButton("Line",   ref fType, 2)) uiState.SelectedFormationType = FormationType.Line;

            if (ImGui.Button("Spawn formation"))
                scenario.SpawnFormation(uiState.SelectedVehicleClass, uiState.SelectedFormationType, _count, uiState.InterpolationMode);

            ImGui.TextColored(new Vector4(0.4f, 1, 0.4f, 1),
                "Hint: Right-click to move selected | Shift+Right-click to add waypoint");
            ImGui.TextDisabled("Drag entities to reposition. Press Delete to destroy.");

            // Preset info
            var preset = VehiclePresets.GetPreset(uiState.SelectedVehicleClass);
            ImGui.Separator();
            ImGui.Text($"Size: {preset.Length:F1} m × {preset.Width:F1} m");
            ImGui.Text($"Max speed: {preset.MaxSpeedFwd:F1} m/s");
        }
    }

    // ─── Combined main UI ─────────────────────────────────────────────────────────

    /// <summary>
    /// Aggregates all SimHost ImGui panels into a single <see cref="Render"/> call.
    /// </summary>
    public class SimHostMainUI
    {
        private readonly SimHostSimulationControlsPanel _simCtrl   = new();
        private readonly SimHostSpawnPanel              _spawnPanel = new();
        public  readonly SimHostUIState                 UIState     = new();

        // Forwarded properties for the visualization facade.
        //
        // `T5`: read-only now, and the setters are gone with the private field they wrote. Nothing
        // ever read them — the panel's pause state is the CLUSTER's, and a slave node cannot set it
        // locally. ConsumeStepRequest went the same way: it had no caller in the repo, and a step is
        // now an intent, not a flag someone must remember to poll.
        public bool  IsPaused  => _simCtrl.IsPaused;
        public float TimeScale => _simCtrl.TimeScale;

        /// <summary>
        /// Supplies the node's cluster transport to the simulation controls. Called by
        /// <c>SimHostSubsystem</c> once the orchestration bus exists — the UI is built before it.
        /// </summary>
        public ITimeTransportFacade? TimeFacade
        {
            get => _simCtrl.TimeFacade;
            set => _simCtrl.TimeFacade = value;
        }

        public void Render(
            EntityRepository          repo,
            ModuleHostKernel          kernel,
            SimHostScenarioManager    scenario,
            SimHostInspectorAdapter   inspector)
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(320, 520), ImGuiCond.FirstUseEver);

            SimHostPanelColors.Push();
            bool ctrlOpen = ImGui.Begin("SimHost Controls");
            SimHostPanelColors.Pop();
            if (ctrlOpen) DrawContent(repo, kernel, scenario);
            ImGui.End();
        }

        /// <summary>
        /// Renders the SimHost controls content without the outer <c>ImGui.Begin/End</c> wrapper.
        /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
        /// </summary>
        public void DrawContent(
            EntityRepository       repo,
            ModuleHostKernel       kernel,
            SimHostScenarioManager scenario)
        {
            if (ImGui.CollapsingHeader("Simulation", ImGuiTreeNodeFlags.DefaultOpen))
                _simCtrl.Render(repo, kernel);

            ImGui.Separator();

            if (ImGui.CollapsingHeader("Spawning", ImGuiTreeNodeFlags.DefaultOpen))
                _spawnPanel.Render(scenario, UIState);
        }
    }
}

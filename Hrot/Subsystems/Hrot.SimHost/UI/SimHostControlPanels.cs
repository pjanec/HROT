using System;
using ImGuiNET;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Trajectory;
using Fdp.Kernel;
using Fdp.ModuleHost.Core;

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

    /// <summary>Play / Pause / Step / Time-Scale controls.</summary>
    public class SimHostSimulationControlsPanel
    {
        public bool  IsPaused    { get; set; }
        public float TimeScale   { get; set; } = 1.0f;
        public bool  StepRequested { get; set; }

        public bool ConsumeStepRequest()
        {
            bool v = StepRequested;
            StepRequested = false;
            return v;
        }

        public void Render(EntityRepository repo, ModuleHostKernel kernel)
        {
            if (ImGui.Button(IsPaused ? "Play  " : "Pause ")) IsPaused = !IsPaused;
            ImGui.SameLine();

            if (ImGui.Button("Step")) StepRequested = true;
            ImGui.SameLine();

            ImGui.SetNextItemWidth(100);
            float ts = TimeScale;
            if (ImGui.SliderFloat("Speed", ref ts, 0.1f, 5.0f)) TimeScale = ts;

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

        // Forwarded properties for the visualization facade
        public bool  IsPaused  { get => _simCtrl.IsPaused;  set => _simCtrl.IsPaused = value; }
        public float TimeScale { get => _simCtrl.TimeScale; set => _simCtrl.TimeScale = value; }
        public bool  ConsumeStepRequest() => _simCtrl.ConsumeStepRequest();

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

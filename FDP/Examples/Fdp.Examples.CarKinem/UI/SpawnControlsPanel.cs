using ImGuiNET;
using System.Numerics;
using Fdp.Examples.CarKinem.Core;
using CarKinem.Core; // VehicleClass
using CarKinem.Formation;
using CarKinem.Trajectory;

namespace Fdp.Examples.CarKinem.UI
{
    // Need a shared state object for UI selections
    public class UIState
    {
        public VehicleClass SelectedVehicleClass { get; set; } = VehicleClass.PersonalCar;
        public FormationType SelectedFormationType { get; set; } = FormationType.Column;
        public TrajectoryInterpolation InterpolationMode { get; set; } = TrajectoryInterpolation.CatmullRom;
    }

    public class SpawnControlsPanel
    {
        private int _spawnCount = 10;
        private bool _randomMovement = true;
        
        public void Render(ScenarioManager scenarioManager, UIState uiState)
        {
            ImGui.SliderInt("Spawn Count", ref _spawnCount, 1, 100);
            ImGui.Checkbox("Random Movement", ref _randomMovement);
            
            // Trajectory Interpolation Toggle
            int mode = (int)uiState.InterpolationMode;
            ImGui.Text("Trajectory Interpolation:");
            if (ImGui.RadioButton("Linear", ref mode, 0)) uiState.InterpolationMode = TrajectoryInterpolation.Linear;
            ImGui.SameLine();
            if (ImGui.RadioButton("Catmull-Rom (Smooth)", ref mode, 1)) uiState.InterpolationMode = TrajectoryInterpolation.CatmullRom;
            
            // Vehicle class combo box
            string[] classNames = Enum.GetNames(typeof(VehicleClass));
            
            int selectedIndex = (int)uiState.SelectedVehicleClass;
            if (ImGui.Combo("Vehicle Type", ref selectedIndex, classNames, classNames.Length))
            {
                uiState.SelectedVehicleClass = (VehicleClass)selectedIndex;
            }
            
            if (ImGui.Button("Spawn Vehicles"))
            {
                // Use generic spawn or Roamers based on checkbox
                if (_randomMovement)
                    scenarioManager.SpawnRoamers(_spawnCount, uiState.SelectedVehicleClass, uiState.InterpolationMode);
                else
                    scenarioManager.SpawnRoadUsers(_spawnCount, uiState.SelectedVehicleClass); // Or similar fallback
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Spawn Collision Test"))
            {
                scenarioManager.SpawnCollisionTest(uiState.SelectedVehicleClass);
            }
            ImGui.SameLine();
            if (ImGui.Button("Spawn Road Users"))
            {
               scenarioManager.SpawnRoadUsers(_spawnCount, uiState.SelectedVehicleClass);
            }
            ImGui.SameLine();
            if (ImGui.Button("Spawn Roamers"))
            {
               scenarioManager.SpawnRoamers(_spawnCount, uiState.SelectedVehicleClass, uiState.InterpolationMode);
            }
            ImGui.SameLine();
            
            if (ImGui.Button("Clear All"))
            {
                // scenarioManager.ClearAll()? 
            }
            
            ImGui.Separator();
            ImGui.Text("Formation Controls");
            // Formation Type
            int fType = (int)uiState.SelectedFormationType;
            ImGui.Text("Type:"); ImGui.SameLine();
            if (ImGui.RadioButton("Column", ref fType, 0)) uiState.SelectedFormationType = FormationType.Column;
            ImGui.SameLine();
            if (ImGui.RadioButton("Wedge", ref fType, 1)) uiState.SelectedFormationType = FormationType.Wedge;
            ImGui.SameLine();
            if (ImGui.RadioButton("Line", ref fType, 2)) uiState.SelectedFormationType = FormationType.Line;
            
            if (ImGui.Button("Spawn Formation"))
            {
                scenarioManager.SpawnFormation(uiState.SelectedVehicleClass, uiState.SelectedFormationType, _spawnCount, uiState.InterpolationMode);
            }
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Hint: Select the Leader to move the entire formation.");
            ImGui.TextDisabled("(Leader marked with 'Leader' label)");
            
            // Show vehicle class info
            var preset = global::CarKinem.Core.VehiclePresets.GetPreset(uiState.SelectedVehicleClass);
            ImGui.Separator();
            ImGui.Text($"Size: {preset.Length:F1}m x {preset.Width:F1}m");
            ImGui.Text($"Max Speed: {preset.MaxSpeedFwd:F1} m/s");
            ImGui.Text($"Max Turn: {(preset.MaxSteerAngle * 180 / MathF.PI):F0}°");
        }
    }
}

using ImGuiNET;
using System.Numerics;
using Fdp.Examples.CarKinem.Core;
using Fdp.Core;
using Fdp.Presentation.Panels;
using Fdp.Core.FlightRecorder;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.ModuleHost;

namespace Fdp.Examples.CarKinem.UI
{
    public class MainUI
    {
        private SpawnControlsPanel _spawnControls = new();
        private SimulationControlsPanel _simControls = new();
        private EntityInspectorPanel _entityInspector = new(); // Framework Panel
        private EventBrowserPanel _eventInspector = new();     // Framework Panel
        private PerformancePanel _perfPanel = new();
        private SystemPerformanceWindow _sysPerfWindow = new();
        
        public UIState UIState { get; } = new();
        
        // Exposed Props for App
        public bool IsPaused 
        { 
            get => _simControls.IsPaused; 
            set => _simControls.IsPaused = value; 
        }
        
        public float TimeScale 
        { 
            get => _simControls.TimeScale;
            set => _simControls.TimeScale = value;
        }

        // Recording / Replay Status
        public bool IsRecording
        {
            get => _simControls.IsRecording;
            set => _simControls.IsRecording = value;
        }

        public bool IsReplaying
        {
            get => _simControls.IsReplaying;
            set => _simControls.IsReplaying = value;
        }

        // Methods to consume toggles
        public bool ConsumeRecordingToggle()
        {
            bool req = _simControls.RecordingToggleInput;
            _simControls.RecordingToggleInput = false;
            return req;
        }
        
        public bool ConsumeReplayToggle()
        {
            bool req = _simControls.ReplayToggleInput;
            _simControls.ReplayToggleInput = false;
            return req;
        }

        public bool ConsumeStepRequest()
        {
            bool req = _simControls.StepRequested;
            _simControls.StepRequested = false;
            return req;
        }

        public void Render(EntityRepository repository, ModuleHostKernel kernel, ScenarioManager scenarioManager, IInspectorContext inspectorCtx, IEnumerable<Fdp.Core.ComponentSystem> systems, PlaybackController? playback = null)
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(300, 500), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Simulation Control"))
            {
                _perfPanel.Render();
                ImGui.Separator();
                
                if (ImGui.CollapsingHeader("Simulation", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    _simControls.Render(repository, kernel, playback);
                }
                
                if (ImGui.CollapsingHeader("Spawning", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    _spawnControls.Render(scenarioManager, UIState);
                }
                
                if (ImGui.CollapsingHeader("Performance"))
                {
                    ImGui.Checkbox("Show System Profiler", ref _sysPerfWindow.IsOpen);
                }
                
                ImGui.End();
            }
            
            // Entity Inspector
            _entityInspector.Draw(new RepositoryAdapter(repository), inspectorCtx);
            
            // Event Inspector
            // Capture events from bus
            _eventInspector.Update(repository.Bus, repository.GlobalVersion);
            _eventInspector.Draw();
            
            // System Performance Profiler
            _sysPerfWindow.Render(systems); 
        }
    }
}

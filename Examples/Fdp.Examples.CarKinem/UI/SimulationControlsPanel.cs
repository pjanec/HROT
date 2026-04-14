using ImGuiNET;
using System.Numerics;
using System;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Core.FlightRecorder;

namespace Fdp.Examples.CarKinem.UI
{
    public class SimulationControlsPanel
    {
        public bool IsPaused { get; set; }
        public float TimeScale { get; set; } = 1.0f;
        
        // Exposed Requests
        public bool StepRequested { get; set; }
        
        // Recording / Replay
        public bool IsRecording { get; set; } 
        public bool IsReplaying { get; set; }
        
        public bool RecordingToggleInput { get; set; } 
        public bool ReplayToggleInput { get; set; }    

        public void Render(EntityRepository repository, ModuleHostKernel kernel, PlaybackController? playback = null)
        {
            // Play/Pause
            if (ImGui.Button(IsPaused ? "Play" : "Pause"))
            {
                IsPaused = !IsPaused;
            }
            
            ImGui.SameLine();
            
            // Step (Only if paused)
            if (ImGui.Button("Step"))
            {
                StepRequested = true;
                // If replaying, step usually moves forward one frame.
                // If simulating, step advances one physics tick.
            }
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            float scale = TimeScale;
            if (ImGui.SliderFloat("Speed", ref scale, 0.1f, 5.0f))
            {
                TimeScale = scale;
            }
            
            ImGui.Separator();

            // Recording Controls
            if (!IsReplaying)
            {
                if (ImGui.Button(IsRecording ? "Stop Recording" : "Record"))
                {
                    RecordingToggleInput = true;
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("Start Replay"))
                {
                    ReplayToggleInput = true;
                }
            }
            else
            {
                if (ImGui.Button("Stop Replay"))
                {
                    ReplayToggleInput = true;
                }
            }

            if (IsReplaying && playback != null)
            {
                // Replay Status
                ImGui.TextColored(new Vector4(1, 1, 0, 1), $"REPLAY MODE [{playback.CurrentFrame}/{playback.TotalFrames}]");
                
                // Timeline Slider
                int currentFrame = playback.CurrentFrame;
                int maxFrame = Math.Max(0, playback.TotalFrames - 1);
                
                ImGui.SetNextItemWidth(-1); // Full width
                if (ImGui.SliderInt("##Timeline", ref currentFrame, 0, maxFrame, "Frame %d"))
                {
                    // User dragged slider - Pause and Seek
                    IsPaused = true;
                    // Seek immediately to provide visual feedback
                    playback.SeekToFrame(repository, currentFrame);
                }
            }
            else if (IsRecording)
            {
               ImGui.TextColored(new Vector4(1, 0, 0, 1), "RECORDING..."); 
            }
            
            ImGui.Separator();
            
            var time = repository.GetSingletonUnmanaged<GlobalTime>();
            ImGui.Text($"Time: {time.TotalTime:F2}s | Frame: {time.FrameNumber}");
            ImGui.Text($"Mode: {kernel.GetTimeController().GetMode()}");
        }
    }
}

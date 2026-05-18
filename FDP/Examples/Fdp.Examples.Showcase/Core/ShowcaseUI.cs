using System;
using System.Linq;
using ImGuiNET;
using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Core
{
    /// <summary>
    /// ImGui-based UI panels for displaying game state, performance, and controls.
    /// </summary>
    public class ShowcaseUI
    {
        private readonly ShowcaseGame _game;

        public ShowcaseUI(ShowcaseGame game)
        {
            _game = game;
        }

        public void DrawAllPanels()
        {
            DrawStatusPanel();
            DrawPerformancePanel();
            DrawControlsPanel();
            
            // Inspectors are drawn separately
            if (_game.ShowInspector)
            {
                _game.Inspector.DrawImGui();
                _game.EventInspector.DrawImGui();
            }
        }

        private void DrawStatusPanel()
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 250), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Status"))
            {
                ref var time = ref _game.Repo.GetSingletonUnmanaged<GlobalTime>();
                
                ImGui.Text($"Time: {time.TotalTime:F2}s");
                ImGui.Text($"Frame: {time.FrameNumber}");
                ImGui.Separator();
                
                // Count entities
                int entityCount = 0;
                var query = _game.Repo.Query().Build();
                foreach (var _ in query) entityCount++;
                
                ImGui.Text($"Entities: {entityCount}");
                ImGui.Separator();
                
                // Mode indicators with colors
                if (_game.IsReplaying)
                {
                    ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Mode: REPLAY");
                }
                else
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "Mode: LIVE");
                }
                
                if (_game.IsRecording)
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "Recording: ON");
                }
                else
                {
                    ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1), "Recording: OFF");
                }
                
                if (_game.IsPaused)
                {
                    ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1), "Paused: YES");
                }
                else
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "Paused: NO");
                }
                
                ImGui.Separator();
                
                // Recording/Playback specific info
                if (_game.IsReplaying && _game.PlaybackController != null)
                {
                    ImGui.Text($"Replay Frame: {_game.PlaybackController.CurrentFrame + 1}/{_game.PlaybackController.TotalFrames}");
                    ImGui.Text($"Rec Tick: {_game.PlaybackController.GetFrameMetadata(_game.PlaybackController.CurrentFrame).Tick}");
                    
                    // Playback progress bar
                    float progress = _game.PlaybackController.TotalFrames > 0 
                        ? (float)_game.PlaybackController.CurrentFrame / _game.PlaybackController.TotalFrames 
                        : 0;
                    ImGui.ProgressBar(progress, new System.Numerics.Vector2(-1, 0), $"{_game.PlaybackController.CurrentFrame + 1}/{_game.PlaybackController.TotalFrames}");
                }
                else if (_game.DiskRecorder != null)
                {
                    ImGui.Text($"Recorded Frames: {_game.DiskRecorder.RecordedFrames}");
                    ImGui.Text($"Dropped Frames: {_game.DiskRecorder.DroppedFrames}");
                }
            }
            ImGui.End();
        }

        private void DrawPerformancePanel()
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 270), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 400), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Performance"))
            {
                // FPS display
                double fps = _game.TotalFrameTime > 0 ? 1000.0 / _game.TotalFrameTime : 0;
                System.Numerics.Vector4 fpsColor = fps switch
                {
                    >= 60 => new System.Numerics.Vector4(0, 1, 0, 1),      // Green
                    >= 30 => new System.Numerics.Vector4(1, 1, 0, 1),      // Yellow
                    >= 20 => new System.Numerics.Vector4(1, 0.5f, 0, 1),   // Orange
                    _ => new System.Numerics.Vector4(1, 0, 0, 1)           // Red
                };
                
                ImGui.TextColored(fpsColor, $"FPS: {fps:F1}");
                ImGui.Text($"Frame Time: {_game.TotalFrameTime:F3} ms");
                ImGui.Separator();
                
                // Phase timings table
                if (ImGui.BeginTable("PhaseTiming", 3, 
                    ImGuiTableFlags.Borders | 
                    ImGuiTableFlags.RowBg | 
                    ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn("Phase", ImGuiTableColumnFlags.WidthFixed, 180);
                    ImGui.TableSetupColumn("Time (ms)", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("%", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableHeadersRow();
                    
                    // Sort phases
                    var orderedPhases = _game.PhaseTimings.OrderBy(kvp => kvp.Key).ToList();
                    
                    double totalLogicTime = orderedPhases
                        .Where(kvp => !kvp.Key.Contains("Render") && !kvp.Key.Contains("Input"))
                        .Sum(kvp => kvp.Value);
                    
                    foreach (var phase in orderedPhases)
                    {
                        string phaseName = phase.Key;
                        double timeMs = phase.Value;
                        double percentage = _game.TotalFrameTime > 0 
                            ? (timeMs / _game.TotalFrameTime) * 100.0 
                            : 0;
                        
                        // Clean up phase name
                        string displayName = phaseName.Length > 3 && char.IsDigit(phaseName[0]) 
                            ? phaseName.Substring(phaseName.IndexOf('.') + 2) 
                            : phaseName;
                        
                        ImGui.TableNextRow();
                        
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text(displayName);
                        
                        ImGui.TableSetColumnIndex(1);
                        System.Numerics.Vector4 timeColor = timeMs switch
                        {
                            > 5.0 => new System.Numerics.Vector4(1, 0, 0, 1),
                            > 2.0 => new System.Numerics.Vector4(1, 1, 0, 1),
                            > 1.0 => new System.Numerics.Vector4(1, 0.5f, 0, 1),
                            > 0.5 => new System.Numerics.Vector4(1, 1, 1, 1),
                            _ => new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1)
                        };
                        ImGui.TextColored(timeColor, $"{timeMs:F3}");
                        
                        ImGui.TableSetColumnIndex(2);
                        ImGui.Text($"{percentage:F1}");
                    }
                    
                    // Totals
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1), "Logic Total");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1), $"{totalLogicTime:F3}");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1), $"{(totalLogicTime / _game.TotalFrameTime * 100):F1}");
                    
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Frame Total");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), $"{_game.TotalFrameTime:F3}");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "100.0");
                    
                    ImGui.EndTable();
                }
            }
            ImGui.End();
        }

        private void DrawControlsPanel()
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 680), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(350, 300), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Controls"))
            {
                ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "General Controls:");
                ImGui.BulletText("ESC - Quit");
                ImGui.BulletText("SPACE - Pause/Resume");
                ImGui.BulletText("R - Toggle Recording");
                ImGui.BulletText("P - Start Playback");
                ImGui.BulletText("I - Toggle Inspector");
                
                ImGui.Separator();
                ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Camera:");
                ImGui.BulletText("WASD / Arrow Keys - Pan");
                ImGui.BulletText("Mouse Wheel - Zoom");
                ImGui.BulletText("Home - Reset Camera");
                
                ImGui.Separator();
                ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Replay Controls:");
                ImGui.BulletText("Left/Right - Seek ±1 frame");
                ImGui.BulletText("SHIFT + Left/Right - Seek ±10 frames");
                ImGui.BulletText("CTRL + Left/Right - Seek ±100 frames");
                ImGui.BulletText("Home/End - First/Last frame");
                
                ImGui.Separator();
                ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Spawn Units:");
                ImGui.BulletText("1 - Spawn Tank");
                ImGui.BulletText("2 - Spawn Aircraft");
                ImGui.BulletText("3 - Spawn Infantry");
                ImGui.BulletText("DELETE - Remove Random Unit");
            }
            ImGui.End();
        }
    }
}

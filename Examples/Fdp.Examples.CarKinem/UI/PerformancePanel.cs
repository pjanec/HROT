using ImGuiNET;
using System.Numerics;
using CarKinem.Systems;

namespace Fdp.Examples.CarKinem.UI
{
    public class PerformancePanel
    {
        // Simple performance stats
        public void Render()
        {
             ImGui.Text($"FPS: {Raylib_cs.Raylib.GetFPS()}");
             ImGui.Text($"Frame Time: {Raylib_cs.Raylib.GetFrameTime() * 1000.0f:F2} ms");
             
             // System stats would require access to Systems list
        }
    }
    
    public class SystemPerformanceWindow
    {
        public bool IsOpen = false;
        
        public void Render(System.Collections.Generic.IEnumerable<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> systems)
        {
            if (!IsOpen) return;
            
            ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("System Performance", ref IsOpen))
            {
                ImGui.Columns(2, "perf_cols", true);
                ImGui.Separator();
                ImGui.Text("System Name"); ImGui.NextColumn();
                ImGui.Text("Time (ms)"); ImGui.NextColumn();
                ImGui.Separator();
                
                foreach(var sys in systems)
                {
                    ImGui.Text(sys.GetType().Name);
                    ImGui.NextColumn();
                    ImGui.Text("N/A");
                    ImGui.NextColumn();
                }
                
                ImGui.Columns(1);
                ImGui.End();
            }
        }
    }
}

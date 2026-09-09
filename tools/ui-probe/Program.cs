using Raylib_cs; using ImGuiNET; using rlImGui_cs;
class P {
  static int Main() {
    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    Raylib.InitWindow(900, 500, "probe");
    if (!Raylib.IsWindowReady()) { System.Console.WriteLine("WINDOW NOT READY"); return 2; }
    rlImGui.Setup(true);
    for (int f = 0; f < 6; f++) {
      Raylib.BeginDrawing(); Raylib.ClearBackground(Color.DarkGray);
      rlImGui.Begin();
      // reproduce the EXACT failing shape: auto-resize popup + stretch column + InputInt
      if (f == 0) ImGui.OpenPopup("probe_modal");
      ImGui.SetNextWindowSize(new System.Numerics.Vector2(520, 0), ImGuiCond.Appearing);
      bool open = true;
      if (ImGui.BeginPopupModal("probe_modal", ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
        if (ImGui.BeginTable("##t", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                       ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit)) {
          ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
          ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
          ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0);
          ImGui.TreeNodeEx("Value", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
          ImGui.TableSetColumnIndex(1);
          float avail = ImGui.GetContentRegionAvail().X;
          float w = avail < 60f ? 60f : avail;
          ImGui.SetNextItemWidth(w);
          int v = 11; ImGui.InputInt("##v", ref v);
          if (f == 5) System.Console.WriteLine($"MEASURED avail={avail:F1} usedWidth={w:F1} itemRect={ImGui.GetItemRectSize().X:F1}");
          ImGui.EndTable();
        }
        ImGui.EndPopup();
      }
      rlImGui.End(); Raylib.EndDrawing();
    }
    Raylib.TakeScreenshot("probe_fixed.png");
    rlImGui.Shutdown(); Raylib.CloseWindow();
    return 0;
  }
}

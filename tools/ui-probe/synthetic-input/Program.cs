using Raylib_cs; using ImGuiNET; using rlImGui_cs; using System.Numerics; using System.Linq;
class P {
  static int Main() {
    // 1. Does ImGui.NET expose input injection at all?
    var io = typeof(ImGuiIOPtr);
    foreach (var m in new[]{"AddMousePosEvent","AddMouseButtonEvent","AddKeyEvent","AddInputCharacter","AddMouseWheelEvent"})
      System.Console.WriteLine($"ImGuiIOPtr.{m}: {(io.GetMethods().Any(x=>x.Name==m) ? "YES" : "NO")}");

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    Raylib.InitWindow(600, 300, "click"); rlImGui.Setup(true);

    int clicks = 0; string typed = ""; Vector2 btnCentre = default;
    for (int f = 0; f < 14; f++) {
      Raylib.BeginDrawing(); Raylib.ClearBackground(Color.DarkGray); rlImGui.Begin();

      // 2. INJECT after rlImGui.Begin() -- i.e. after the backend pushed real input.
      var g = ImGui.GetIO();
      if (f >= 4 && btnCentre != default) g.AddMousePosEvent(btnCentre.X, btnCentre.Y);
      if (f == 6) g.AddMouseButtonEvent(0, true);
      if (f == 7) g.AddMouseButtonEvent(0, false);
      if (f == 10) { g.AddInputCharacter('4'); g.AddInputCharacter('2'); }

      ImGui.SetNextWindowPos(new Vector2(20,20)); ImGui.SetNextWindowSize(new Vector2(400,150));
      ImGui.Begin("driver");
      if (ImGui.Button("Press me")) clicks++;
      var mn = ImGui.GetItemRectMin(); var mx = ImGui.GetItemRectMax();
      btnCentre = new Vector2((mn.X+mx.X)/2, (mn.Y+mx.Y)/2);   // 3. the app tells us WHERE
      if (f == 9) ImGui.SetKeyboardFocusHere();
      ImGui.InputText("##t", ref typed, 16);
      ImGui.Text($"clicks={clicks} typed='{typed}'");
      ImGui.End();
      rlImGui.End(); Raylib.EndDrawing();
    }
    Raylib.TakeScreenshot("click.png");
    System.Console.WriteLine($"RESULT clicks={clicks} typed='{typed}' buttonRectCentre={btnCentre}");
    rlImGui.Shutdown(); Raylib.CloseWindow();
    return clicks > 0 ? 0 : 3;
  }
}

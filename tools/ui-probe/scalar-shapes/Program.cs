using Raylib_cs; using ImGuiNET; using rlImGui_cs; using System.Numerics;
using StructEdit.Core; using Fdp.Presentation.Editing; using Hrot.Editor.AiShared.Inspector; using Hrot.Editor.AiShared.Blackboard;
class P {
  static void Table(string id, System.Action body) {
    if (ImGui.BeginTable(id, 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit)) {
      ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
      ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
      body(); ImGui.EndTable();
    }
  }
  static int Main() {
    var svc = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();
    var entry = new BlackboardVariableEntry("Count", typeof(int), Comment: null, DefaultValueJson: "11");
    using var s1 = DefaultValueAuthoring.OpenSession(svc, entry);
    using var s2 = DefaultValueAuthoring.OpenSession(svc, entry);
    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    Raylib.InitWindow(1000, 420, "scalar shapes"); rlImGui.Setup(true);
    for (int f = 0; f < 6; f++) {
      Raylib.BeginDrawing(); Raylib.ClearBackground(Color.DarkGray); rlImGui.Begin();
      ImGui.SetNextWindowPos(new Vector2(40, 60)); ImGui.SetNextWindowSize(new Vector2(430, 160));
      ImGui.Begin("A - TODAY: DrawEditNode(Root)", ImGuiWindowFlags.NoResize);
      Table("##a", () => new ComponentEditDrawer(s1, null)
                            .DrawEditNode(s1.Document.Root));
      ImGui.End();
      var leaf = s1.Document.Root.Children[0];
      ImGui.SetNextWindowPos(new Vector2(510, 60)); ImGui.SetNextWindowSize(new Vector2(430, 160));
      ImGui.Begin("B - PROPOSED: renamed leaf", ImGuiWindowFlags.NoResize);
      var l2 = s2.Document.Root.Children[0];
      Table("##b", () => new ComponentEditDrawer(s2, null)
                            .DrawEditNode(new EditNode(l2.Id, "Count", l2.JsonPath, l2.Kind,
                                                       l2.ClrType, l2.Binding, l2.Children,
                                                       l2.Metadata, l2.IsReadOnly)));
      ImGui.End();
      if (f == 5) System.Console.WriteLine($"root='{s1.Document.Root.Name}' childCount={s1.Document.Root.Children.Count} leaf='{leaf.Name}' leafType={leaf.ClrType.Name}");
      rlImGui.End(); Raylib.EndDrawing();
    }
    Raylib.TakeScreenshot("scalar.png");
    System.Console.WriteLine("committed A = " + ScalarEditBox.Unwrap(s1.Commit(), typeof(int)));
    rlImGui.Shutdown(); Raylib.CloseWindow(); return 0;
  }
}

using Raylib_cs;
using rlImGui_cs;

namespace NodeEditor.Demo;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 1000, "NodeEditor Demo");
        Raylib.SetTargetFPS(60);

        rlImGui.Setup(darkTheme: true, enableDocking: true);

        var demo = new DemoShell();
        double lastTime = Raylib.GetTime();

        while (!Raylib.WindowShouldClose())
        {
            double now     = Raylib.GetTime();
            double elapsed = now - lastTime;
            lastTime = now;

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(26, 26, 26, 255));

            rlImGui.Begin();
            demo.Frame(elapsed);
            rlImGui.End();

            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}

using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace NodeEditor.Demo;

internal static class Program
{
    // Candidate paths for Arial (or an equivalent).  We fall back through the list
    // until one resolves so the demo works for both per-user and system font installs.
    // Environment.SpecialFolder.Fonts returns an empty string on Linux, so the first
    // entry harmlessly fails to resolve there and we fall through to common Linux
    // TrueType font locations (DejaVu Sans ships with most distributions).
    private static readonly string[] ArialCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
        @"C:\Windows\Fonts\arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/msttcorefonts/Arial.ttf",
    ];

    [STAThread]
    private static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 1000, "NodeEditor Demo");
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(60);

        rlImGui.Setup(darkTheme: true, enableDocking: true);

        // ── Font pipeline ────────────────────────────────────────────────────
        // Fonts must be added to the atlas BEFORE it is baked and uploaded to
        // the GPU.  rlImGui.Setup already baked a default atlas, so we rebuild
        // it here with the additional TrueType faces.
        //
        // The first font added becomes the implicit default for all ImGui widgets
        // (menus, panels, etc.).  The scaled canvas fonts are only applied via
        // explicit PushFont/PopFont blocks inside NodeRenderer and PinRenderer.
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

        // Always keep the built-in pixel font as the global default so that all
        // structural UI (menus, toolbars, panels) continues to use it unchanged.
        io.Fonts.AddFontDefault();

        // Resolve Arial and bake it at four discrete sizes for crisp text
        // across the typical canvas zoom range (0.3× – 3×).
        // The 11px face covers the zoomed-out range where 16px would be
        // downscaled and pixelate; each step up is roughly 1.45× larger.
        string? arialPath = ArialCandidates.FirstOrDefault(File.Exists);

        var fonts = new Dictionary<float, nint>();

        if (arialPath is not null)
        {
            // Include the default Latin + extended Latin + Cyrillic ranges so
            // node titles and pin labels render correctly for a wide UTF-8 corpus.
            nint glyphRanges = io.Fonts.GetGlyphRangesDefault();

            unsafe
            {
                fonts[8f] = (nint)io.Fonts.AddFontFromFileTTF(arialPath, 8f, null, glyphRanges).NativePtr;
                fonts[11f] = (nint)io.Fonts.AddFontFromFileTTF(arialPath, 11f, null, glyphRanges).NativePtr;
                fonts[16f] = (nint)io.Fonts.AddFontFromFileTTF(arialPath, 16f, null, glyphRanges).NativePtr;
                fonts[24f] = (nint)io.Fonts.AddFontFromFileTTF(arialPath, 24f, null, glyphRanges).NativePtr;
                fonts[32f] = (nint)io.Fonts.AddFontFromFileTTF(arialPath, 32f, null, glyphRanges).NativePtr;
            }
        }
        // If Arial is absent the dictionary stays empty and the theme falls
        // back to the default ImGui font automatically.

        // Re-upload the augmented atlas to the GPU.
        rlImGui.ReloadFonts();
        // ────────────────────────────────────────────────────────────────────

        var demo = new DemoShell(fonts);
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

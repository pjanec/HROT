using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Hrot.ClusterRunner.Presentation;

internal sealed class RaylibPresentationShell : IPresentationShell
{
    private Raylib_cs.Texture2D _atlasTexture;
    private Raylib_cs.Font _gizmoFont;
    private IntPtr _iniFilenamePtr;

    /// <inheritdoc/>
    public Fdp.Presentation.Fonts.EditorFontService FontService { get; } = new();

    public void InitWindow(int width, int height, string title, int targetFps)
    {
        // HighDpiWindow lets Raylib report the true monitor content scale via
        // GetWindowScaleDPI(), which drives the editor's DPI-aware font baking.
        Raylib_cs.Raylib.SetConfigFlags(
            Raylib_cs.ConfigFlags.ResizableWindow | Raylib_cs.ConfigFlags.HighDpiWindow);
        Raylib_cs.Raylib.InitWindow(width, height, title);
        Raylib_cs.Raylib.SetExitKey(Raylib_cs.KeyboardKey.Null);
        Raylib_cs.Raylib.SetTargetFPS(targetFps);
    }

    public void SetupImGui()
    {
        rlImGui_cs.rlImGui.Setup(true);
        var io = ImGuiNET.ImGui.GetIO();
        io.ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string configDir = Path.Combine(appData, "HROT");
        Directory.CreateDirectory(configDir);
        string iniPath = Path.Combine(configDir, "imgui.ini");
        _iniFilenamePtr = Marshal.StringToHGlobalAnsi(iniPath);
        unsafe
        {
            io.NativePtr->IniFilename = (byte*)_iniFilenamePtr;
        }

        // Bake the editor fonts (Roboto UI face + FontAwesome + canvas ladder) at the
        // autodetected monitor DPI. The persisted user UI-scale multiplier is applied
        // later (LocalWindowController) once window settings have loaded — that queues a
        // one-off rebuild on the first frame if it differs from 1.0.
        float dpi = Raylib_cs.Raylib.GetWindowScaleDPI().X;
        FontService.Initialize(dpiScale: dpi, userScale: 1f);
    }

    public void ShutdownImGui()
    {
        rlImGui_cs.rlImGui.Shutdown();
        if (_iniFilenamePtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_iniFilenamePtr);
            _iniFilenamePtr = IntPtr.Zero;
        }
    }

    public void CloseWindow()
    {
        if (_atlasTexture.Id != 0)
            Raylib_cs.Raylib.UnloadTexture(_atlasTexture);
        if (_gizmoFont.Texture.Id != 0)
            Raylib_cs.Raylib.UnloadFont(_gizmoFont);
        Raylib_cs.Raylib.CloseWindow();
    }

    public void UnloadAtlasTexture()
    {
        if (_atlasTexture.Id != 0)
        {
            Raylib_cs.Raylib.UnloadTexture(_atlasTexture);
            _atlasTexture = default;
        }
    }

    public Fdp.Presentation.Icons.IconAtlas LoadIconAtlas()
    {
        byte[] pngBytes = Fdp.Presentation.Icons.EmbeddedAtlasResources.GetSilkAtlasPngBytes();
        var img = Raylib_cs.Raylib.LoadImageFromMemory(".png", pngBytes);
        _atlasTexture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        // Bilinear filtering so 16x16 silk cells resample smoothly when drawn larger than
        // native (DPI-scaled menu gutters, the blueprint node picker, etc.) instead of the
        // default nearest/point sampling that shows blocky pixels on upscale. At 1:1 (16px)
        // it is identical to point sampling, so no downside for native-size icons.
        Raylib_cs.Raylib.SetTextureFilter(_atlasTexture, Raylib_cs.TextureFilter.Bilinear);
        Raylib_cs.Raylib.UnloadImage(img);
        return new Fdp.Presentation.Icons.IconAtlas(
            (nint)_atlasTexture.Id, _atlasTexture.Width, _atlasTexture.Height, 16f);
    }

    public void LoadGizmoFont()
    {
        byte[] ttf = Fdp.Presentation.Fonts.EmbeddedFontResources.GetRobotoRegularTtfBytes();
        // Load at 32-pixel base size for crisp downscaling to 9–13 px gizmo text.
        // glyphCount = 0 → load the default character set (covers ASCII + Latin).
        _gizmoFont = Raylib_cs.Raylib.LoadFontFromMemory(".ttf", ttf, 32, null, 0);
        Raylib_cs.Raylib.SetTextureFilter(_gizmoFont.Texture, Raylib_cs.TextureFilter.Bilinear);
        GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont = _gizmoFont;
    }
}

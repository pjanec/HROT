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
        // NOTE: intentionally NOT setting ConfigFlags.HighDpiWindow. With it, Raylib creates a
        // GL framebuffer at the physical (DPI-scaled) resolution while the ImGui integration
        // (rlImGui_cs) still lays out at the logical window size, so on a hi-DPI monitor the UI
        // renders clipped into a sub-rect with the rest blank. We keep a 1:1 framebuffer and get
        // readability from larger baked fonts + the UI-scale slider instead. Revisit if/when the
        // ImGui backend gains proper framebuffer-scale handling.
        Raylib_cs.Raylib.SetConfigFlags(Raylib_cs.ConfigFlags.ResizableWindow);
        Raylib_cs.Raylib.InitWindow(width, height, title);
        Raylib_cs.Raylib.SetExitKey(Raylib_cs.KeyboardKey.Null);
        Raylib_cs.Raylib.SetTargetFPS(targetFps);
        TrySetWindowIcon();
    }

    /// <summary>
    /// Sets the window/taskbar icon from the embedded <c>HROT-icon-green.png</c> (see .csproj).
    /// Best-effort: any failure (missing resource, decode error) is swallowed so the window still
    /// opens with the default icon. Must be called after <see cref="Raylib_cs.Raylib.InitWindow"/>.
    /// </summary>
    private static void TrySetWindowIcon()
    {
        try
        {
            using var stream = typeof(RaylibPresentationShell).Assembly
                .GetManifestResourceStream("Hrot.ClusterRunner.HROT-icon-green.png");
            if (stream == null) return;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var pngBytes = ms.ToArray();

            var image = Raylib_cs.Raylib.LoadImageFromMemory(".png", pngBytes);
            // GLFW wants RGBA8 pixel data; normalise so any PNG encoding works.
            Raylib_cs.Raylib.ImageFormat(ref image, Raylib_cs.PixelFormat.UncompressedR8G8B8A8);
            // SetWindowIcon copies the pixels into GLFW, so the image can be freed immediately.
            Raylib_cs.Raylib.SetWindowIcon(image);
            Raylib_cs.Raylib.UnloadImage(image);
        }
        catch
        {
            // Non-fatal — keep the default icon.
        }
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

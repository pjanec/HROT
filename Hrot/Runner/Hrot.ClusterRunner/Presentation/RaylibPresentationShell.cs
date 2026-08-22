using System;
using System.IO;
using System.Runtime.InteropServices;
using Fdp.Core.Logging;

namespace Hrot.ClusterRunner.Presentation;

internal sealed class RaylibPresentationShell : IPresentationShell
{
    /// <summary>
    /// ⭐⭐ <b>The user layout folder's name</b> — 📌 <c>LayoutPaths</c> takes it as an argument so
    /// <c>Fdp.Presentation</c> never learns what "HROT" is *(the design's constraint 1)*.
    /// ⭐ <c>internal</c> so <c>LocalWindowController</c> names the same folder the ini went to, ⛔ rather
    /// than repeating the literal and re-creating the split this batch is closing.
    /// </summary>
    internal const string AppFolderName = "HROT";

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
        TryApplyTaskbarIcons();
    }

    // ── Windows taskbar icon (console + GUI windows) ─────────────────────────────

    private const uint WM_SETICON = 0x0080;
    private const nint ICON_SMALL = 0;
    private const nint ICON_BIG   = 1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string exeFileName, int iconIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Forces the HROT icon onto the Windows taskbar button(s). A console-subsystem exe gets a
    /// separate console window whose taskbar button carries the generic conhost icon (GLFW's
    /// SetWindowIcon only styles the GUI window) — so we WM_SETICON the console window too, plus the
    /// GUI window, using the icon already embedded in the exe (ApplicationIcon). Best-effort/no-throw.
    /// </summary>
    private static void TryApplyTaskbarIcons()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            IntPtr hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
            if (hIcon == IntPtr.Zero || hIcon == new IntPtr(1)) return; // 1 = no icons in file

            IntPtr guiHwnd;
            unsafe { guiHwnd = (IntPtr)Raylib_cs.Raylib.GetWindowHandle(); }

            foreach (var hwnd in new[] { GetConsoleWindow(), guiHwnd })
            {
                if (hwnd == IntPtr.Zero) continue;
                SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon);
                SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon);
            }
        }
        catch
        {
            // Non-fatal — leave whatever icon Windows chose.
        }
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
            // Pad to a SQUARE canvas before handing to GLFW. SetWindowIcon derives both the small
            // (title-bar) and big (TASKBAR) icons by scaling this one image; a non-square source
            // scales cleanly to the small icon but yields a broken/blank big icon — which is why the
            // title bar showed the icon while the taskbar fell back to the default.
            int side = System.Math.Max(image.Width, image.Height);
            if (image.Width != image.Height)
            {
                Raylib_cs.Raylib.ImageResizeCanvas(
                    ref image, side, side,
                    (side - image.Width) / 2, (side - image.Height) / 2,
                    Raylib_cs.Color.Blank); // transparent padding
            }
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

        // ⭐⭐ Batch 103 (103a) — the same helper FdpApplication uses. 📌 See its remark: the two
        //    computations were byte-identical and independent.
        string iniPath = Fdp.Presentation.WindowManager.LayoutPaths.UserIniPath(AppFolderName);
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
        // Dimensions from the DECODED image (a CPU operation, valid even if the GPU upload below
        // fails) so the atlas UV math has a correct, non-zero divisor regardless.
        int atlasWidth = img.Width, atlasHeight = img.Height;
        _atlasTexture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);

        // A headless machine whose Xvfb provides no GL driver returns texture id 0 here — the
        // "null icon-atlas handle" case. It is not fatal: raylib's id 0 is the built-in 1x1 white
        // texture, so icons draw as blank quads rather than crashing. Say so once, keep the decoded
        // dimensions, and run on. (With a real GL context — hardware or Xvfb+Mesa/llvmpipe — id != 0
        // and this path is not taken.)
        if (_atlasTexture.Id == 0)
        {
            FdpLog<RaylibPresentationShell>.Warn(
                "[Icons] The icon atlas texture failed to upload (GPU texture id 0). The editor will "
              + "run with blank icons. On a headless Linux host, launch under Xvfb with a GL driver "
              + "(e.g. LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe xvfb-run …).");
            return new Fdp.Presentation.Icons.IconAtlas((nint)0, atlasWidth, atlasHeight, 16f);
        }

        // Bilinear filtering so 16x16 silk cells resample smoothly when drawn larger than
        // native (DPI-scaled menu gutters, the blueprint node picker, etc.) instead of the
        // default nearest/point sampling that shows blocky pixels on upscale. At 1:1 (16px)
        // it is identical to point sampling, so no downside for native-size icons.
        Raylib_cs.Raylib.SetTextureFilter(_atlasTexture, Raylib_cs.TextureFilter.Bilinear);
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

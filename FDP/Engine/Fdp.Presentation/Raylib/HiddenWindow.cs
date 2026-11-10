using System;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Fdp.Presentation.Raylib;

/// <summary>
/// ⭐ CE-4386 (handoff L-0) — the HIDDEN-WINDOW mode, one switch: <c>HROT_HIDDEN_WINDOW=1</c> makes every window creator (the frame-rail harness, the editor / player shell)
/// create its window HIDDEN and UNFOCUSED — no window appears, takes focus or shows in the taskbar (🔒 user: "I need to do other light work on that machine and do not want to
/// be annoyed by popping windows"). ⭐ Because a hidden window's own framebuffer is not reliably readable on every driver, each frame is drawn into an off-screen render target of
/// the window's size (<see cref="BeginFrame"/> / <see cref="EndFrame"/>), and after the frame that target stays the GL READ framebuffer (<see cref="AfterFrame"/>) — so every
/// <c>Raylib.LoadImageFromScreen()</c> caller reads the frame unchanged. ⛔ Default OFF: the normal visible window, nothing redirected.
/// </summary>
public static class HiddenWindow
{
    public const string EnvVar = "HROT_HIDDEN_WINDOW";
    private const int GlReadFramebuffer = 0x8CA8, GlFramebuffer = 0x8D40;

    private static RenderTexture2D _target;
    private static bool _hasTarget, _inFrame;

    /// <summary>True when the switch asks for a hidden window (read once per call; set it before the window is created).</summary>
    public static bool Requested => Environment.GetEnvironmentVariable(EnvVar) == "1";

    /// <summary>True once a hidden window was created through <see cref="ApplyBeforeInit"/> — the frame redirection is on.</summary>
    public static bool Active { get; private set; }

    /// <summary>How many frames went through the render target (a rail).</summary>
    public static int FramesRedirected { get; private set; }

    /// <summary>Call right before <c>InitWindow</c>: in hidden mode adds the hidden + unfocused flags (raylib ORs config flags). Returns whether it is hidden.</summary>
    public static bool ApplyBeforeInit()
    {
        Active = Requested;
        if (Active) Raylib_cs.Raylib.SetConfigFlags(ConfigFlags.HiddenWindow | ConfigFlags.UnfocusedWindow);
        return Active;
    }

    /// <summary>Call right after <c>InitWindow</c>: in hidden mode makes sure the window stays hidden, and hides a console window this process created for itself
    /// (Windows; never a console it shares with the terminal that started it).</summary>
    public static void AfterInit()
    {
        if (!Active) return;
        if (!Raylib_cs.Raylib.IsWindowHidden()) Raylib_cs.Raylib.SetWindowState(ConfigFlags.HiddenWindow);
        if (OperatingSystem.IsWindows()) HideOwnConsole();
        Console.Error.WriteLine($"[HiddenWindow] {EnvVar}=1: window hidden={Raylib_cs.Raylib.IsWindowHidden()} focused={Raylib_cs.Raylib.IsWindowFocused()} — frames drawn off screen");   // one line, the run's proof
    }

    /// <summary>Call right after <c>BeginDrawing</c>: in hidden mode the frame draws into the render target (made or resized to the window's size).</summary>
    public static void BeginFrame()
    {
        if (!Active) return;
        int w = Math.Max(1, Raylib_cs.Raylib.GetScreenWidth()), h = Math.Max(1, Raylib_cs.Raylib.GetScreenHeight());
        if (!_hasTarget || _target.Texture.Width != w || _target.Texture.Height != h)
        {
            if (_hasTarget) Raylib_cs.Raylib.UnloadRenderTexture(_target);
            _target = Raylib_cs.Raylib.LoadRenderTexture(w, h);
            _hasTarget = true;
        }
        Raylib_cs.Raylib.BeginTextureMode(_target);
        _inFrame = true;
    }

    /// <summary>A pass that used its OWN render target inside a frame ends it by binding the window again: this puts the frame back on the hidden target.</summary>
    public static void Resume()
    {
        if (Active && _inFrame) Raylib_cs.Raylib.BeginTextureMode(_target);
    }

    /// <summary>Call right before <c>EndDrawing</c>.</summary>
    public static void EndFrame()
    {
        if (!Active || !_inFrame) return;
        Raylib_cs.Raylib.EndTextureMode();
        _inFrame = false;
        FramesRedirected++;
    }

    /// <summary>Call right after <c>EndDrawing</c>: the render target becomes the READ framebuffer, so <c>LoadImageFromScreen</c> reads the frame just drawn.</summary>
    public static void AfterFrame()
    {
        if (!Active || !_hasTarget) return;
        Rlgl.BindFramebuffer(GlReadFramebuffer, _target.Id);
    }

    /// <summary>Before the window closes: frees the target (its GL context goes with the window).</summary>
    public static void Release()
    {
        if (_hasTarget) { Rlgl.BindFramebuffer(GlFramebuffer, 0); Raylib_cs.Raylib.UnloadRenderTexture(_target); }
        _hasTarget = false; _inFrame = false;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList(uint[] list, uint count);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    private static void HideOwnConsole()
    {
        try
        {
            var console = GetConsoleWindow();
            if (console == IntPtr.Zero) return;
            var list = new uint[4];
            if (GetConsoleProcessList(list, (uint)list.Length) == 1) ShowWindow(console, 0);   // SW_HIDE — only a console no other process (the user's terminal) shares
        }
        catch (Exception) { /* best effort */ }
    }
}

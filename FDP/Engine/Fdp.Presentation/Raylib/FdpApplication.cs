using System;
using System.IO;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost;
using Raylib_cs;
using rlImGui_cs;

namespace Fdp.Presentation.Raylib;

/// <summary>
/// Abstract base class for FDP applications with Raylib rendering.
/// Provides standardized lifecycle: Load → [Update → Draw] → Unload
/// </summary>
public abstract class FdpApplication : IDisposable
{
    protected readonly ApplicationConfig Config;
    
    // Core FDP components (User initializes these in OnLoad)
    protected EntityRepository World { get; set; } = null!;
    protected ModuleHostKernel Kernel { get; set; } = null!;

    public FdpApplication(ApplicationConfig config)
    {
        Config = config;
    }

    private bool _shouldQuit = false;
    private IntPtr _iniFilenamePtr;

    /// <summary>
    /// Signals the application to close at the end of the current frame.
    /// </summary>
    public void Quit()
    {
        _shouldQuit = true;
    }

    /// <summary>
    /// Main application loop. Call this from your Main() method.
    /// </summary>
    public void Run()
    {
        InitializeWindow();
        
        // 1. User Initialization
        OnLoad();

        // Remote-desktop clicks (TeamViewer / Parsec / RDP) inject WM_*BUTTONDOWN and
        // WM_*BUTTONUP microseconds apart, so both land in one glfwPollEvents() drain and the
        // polled button state ends where it started -- the press is never observed and the click
        // is silently lost. The latch replays a lost click held across frames. Inert for local
        // input and a no-op off Windows; kill switch: HROT_DISABLE_CLICK_LATCH=1.
        using var clickLatch = Input.ClickLatch.Create();

        // 2. Main Loop
        while (!Raylib_cs.Raylib.WindowShouldClose() && !_shouldQuit)
        {
            // Before input is polled: replay anything the previous frame dropped.
            clickLatch.Tick(
                Raylib_cs.Raylib.IsMouseButtonDown(Raylib_cs.MouseButton.Left),
                Raylib_cs.Raylib.IsMouseButtonDown(Raylib_cs.MouseButton.Right),
                Raylib_cs.Raylib.IsMouseButtonDown(Raylib_cs.MouseButton.Middle));

            float dt = Raylib_cs.Raylib.GetFrameTime();

            // A. Logic Update
            OnUpdate(dt);

            // B. Rendering
            Raylib_cs.Raylib.BeginDrawing();
            Raylib_cs.Raylib.ClearBackground(Color.DarkGray);

            // B1. World Rendering (2D/3D)
            OnDrawWorld();

            // B2. UI Rendering (ImGui)
            rlImGui.Begin();
            OnDrawUI();
            rlImGui.End();

            Raylib_cs.Raylib.EndDrawing();
        }

        // 3. Cleanup
        OnUnload();
        ShutdownWindow();
    }

    private void InitializeWindow()
    {
        Raylib_cs.Raylib.SetConfigFlags(Config.Flags);
        Raylib_cs.Raylib.InitWindow(Config.Width, Config.Height, Config.WindowTitle);
        Raylib_cs.Raylib.SetTargetFPS(Config.TargetFPS);
        
        rlImGui.Setup(true); // Enable Docking by default

        if (Config.PersistenceEnabled)
        {
            var io = ImGuiNET.ImGui.GetIO();
            io.ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;

            // ⭐⭐ Batch 103 (103a) — ONE owner of the convention. 📐 This block and
            //    RaylibPresentationShell.SetupImGui computed the same path INDEPENDENTLY, byte for byte:
            //    "two apps, one convention, no shared helper." ⛔ Two copies of a path is how a reset
            //    ends up resetting one of them.
            // ⭐ The NAME stays here, the CONVENTION moves — Fdp.Presentation must not learn what
            //   "HROT" is (the design's constraint 1), so it travels as an argument.
            string iniPath = Fdp.Presentation.WindowManager.LayoutPaths.UserIniPath("HROT");
            _iniFilenamePtr = Marshal.StringToHGlobalAnsi(iniPath);
            unsafe
            {
                io.NativePtr->IniFilename = (byte*)_iniFilenamePtr;
            }
        }
    }

    private void ShutdownWindow()
    {
        rlImGui.Shutdown();
        if (_iniFilenamePtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_iniFilenamePtr);
            _iniFilenamePtr = IntPtr.Zero;
        }
        Raylib_cs.Raylib.CloseWindow();
    }

    // --- Lifecycle Methods to Override ---

    /// <summary>
    /// Initialize your EntityRepository, ModuleHostKernel, and register modules here.
    /// </summary>
    protected abstract void OnLoad();

    /// <summary>
    /// Called every frame before rendering. 
    /// Usually calls Kernel.Update() and handles custom input logic.
    /// </summary>
    protected virtual void OnUpdate(float dt)
    {
        Kernel?.Update(); // Default behavior
    }

    /// <summary>
    /// Render your game world here (e.g. MapCanvas). 
    /// ImGui is NOT active in this pass (pure Raylib).
    /// </summary>
    protected abstract void OnDrawWorld();

    /// <summary>
    /// Render your Inspectors and Panels here.
    /// ImGui context IS active here.
    /// </summary>
    protected abstract void OnDrawUI();

    /// <summary>
    /// Dispose resources (Kernel, World, etc).
    /// </summary>
    protected virtual void OnUnload()
    {
        Kernel?.Dispose();
        World?.Dispose();
    }

    public void Dispose()
    {
        // Safety dispose if Run() wasn't called or exited abnormally
        OnUnload();
    }
}

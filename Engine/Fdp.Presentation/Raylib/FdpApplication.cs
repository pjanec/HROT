using Fdp.Kernel;
using ModuleHost.Core;
using Raylib_cs;
using rlImGui_cs;

namespace FDP.Framework.Raylib;

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

        // 2. Main Loop
        while (!Raylib_cs.Raylib.WindowShouldClose() && !_shouldQuit)
        {
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
        // TODO: Load persistence logic here if Config.PersistenceEnabled
        
        Raylib_cs.Raylib.SetConfigFlags(Config.Flags);
        Raylib_cs.Raylib.InitWindow(Config.Width, Config.Height, Config.WindowTitle);
        Raylib_cs.Raylib.SetTargetFPS(Config.TargetFPS);
        
        rlImGui.Setup(true); // Enable Docking by default
    }

    private void ShutdownWindow()
    {
        // TODO: Save persistence logic here if Config.PersistenceEnabled
        
        rlImGui.Shutdown();
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

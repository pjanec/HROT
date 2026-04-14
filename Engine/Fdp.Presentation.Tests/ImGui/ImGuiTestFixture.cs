using System;
using System.Numerics;
using System.Threading;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Toolkit.ImGui.Tests;

/// <summary>
/// A headless ImGui context for testing.
/// Initializes the context, builds a font atlas, and sets up the display size.
/// </summary>
/// <remarks>
/// A process-wide semaphore serializes all fixture instances to prevent races on
/// the ImGui native global state when multiple test runners (e.g. VS Code test
/// explorer + terminal) share the same process via the ServiceHub test host.
/// </remarks>
public class ImGuiTestFixture : IDisposable
{
    // Named mutex so that even two separate vstest processes on the same machine
    // (unlikely but safe) cannot create contexts simultaneously.
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private IntPtr _context;
    
    public ImGuiTestFixture()
    {
        _semaphore.Wait();
        try
        {
            // Creates the ImGui context
            _context = ImGuiApi.CreateContext();
            ImGuiApi.SetCurrentContext(_context);
            
            // Setup style/IO to prevent crashes
            var io = ImGuiApi.GetIO();
            io.DisplaySize = new Vector2(1024, 768);
            io.DeltaTime = 1.0f / 60.0f;
            
            // Required for any text size calculation (otherwise size is 0 and layout is broken)
            io.Fonts.AddFontDefault(); 
            io.Fonts.Build();
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }
    
    public void NewFrame()
    {
        ImGuiApi.NewFrame();
    }
    
    public void Render()
    {
        ImGuiApi.Render();
    }
    
    public void Dispose()
    {
        ImGuiApi.DestroyContext(_context);
        _semaphore.Release();
    }
}

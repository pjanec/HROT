using System;
using System.Numerics;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace FDP.Toolkit.ImGui.Tests;

/// <summary>
/// A headless ImGui context for testing.
/// Initializes the context, builds a font atlas, and sets up the display size.
/// </summary>
public class ImGuiTestFixture : IDisposable
{
    private IntPtr _context;
    
    public ImGuiTestFixture()
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
    }
}

using Raylib_cs;

namespace FDP.Framework.Raylib;

/// <summary>
/// Configuration for FdpApplication windowing and setup.
/// </summary>
public struct ApplicationConfig
{
    public string WindowTitle { get; set; } = "FDP Application";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int TargetFPS { get; set; } = 60;
    public ConfigFlags Flags { get; set; } = ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint;
    
    /// <summary>
    /// If true, attempts to load/save window position and size to "window.config"
    /// </summary>
    public bool PersistenceEnabled { get; set; } = true;

    public ApplicationConfig() { }
}

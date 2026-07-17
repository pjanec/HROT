namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// Testable seam for Raylib and ImGui window operations.
/// </summary>
internal interface IPresentationShell
{
    void InitWindow(int width, int height, string title, int targetFps);
    void SetupImGui();

    /// <summary>
    /// The editor font pipeline (Roboto UI face + FontAwesome merge + canvas ladder +
    /// DPI / UI-scale). Populated by <see cref="SetupImGui"/>; used by the render loop to
    /// apply queued scale changes and by the Settings UI to drive live rescaling.
    /// </summary>
    Fdp.Presentation.Fonts.EditorFontService FontService { get; }
    void ShutdownImGui();
    void CloseWindow();
    void UnloadAtlasTexture();
    Fdp.Presentation.Icons.IconAtlas LoadIconAtlas();

    /// <summary>
    /// Loads the embedded Roboto TTF into a Raylib Font and registers it on
    /// <see cref="GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont"/>.
    /// Must be called after the GL context exists (i.e. after InitWindow).
    /// </summary>
    void LoadGizmoFont();
}

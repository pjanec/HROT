namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// Testable seam for Raylib and ImGui window operations.
/// </summary>
internal interface IPresentationShell
{
    void InitWindow(int width, int height, string title, int targetFps);
    void SetupImGui();
    void ShutdownImGui();
    void CloseWindow();
    void UnloadAtlasTexture();
    Fdp.Presentation.Icons.IconAtlas LoadIconAtlas();
}

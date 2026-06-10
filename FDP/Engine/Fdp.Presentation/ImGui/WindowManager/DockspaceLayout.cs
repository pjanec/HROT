using System.Numerics;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// Pure static helper that computes the central dockspace position and size
/// given the work area and the top toolbar + bottom status-bar insets (§4.1.2).
/// No ImGui dependency — suitable for headless unit testing and the host
/// dockspace setup in <c>Program.cs</c>.
/// </summary>
public static class DockspaceLayout
{
    /// <summary>
    /// Central dockspace size given the work area and the top toolbar +
    /// bottom status-bar insets.
    /// <c>Width = workWidth</c>.
    /// <c>Height = workHeight - toolbarHeight - statusBarHeight</c>, clamped to ≥ 0.
    /// </summary>
    public static Vector2 CentralSize(float workWidth, float workHeight, float toolbarHeight, float statusBarHeight)
    {
        float h = workHeight - toolbarHeight - statusBarHeight;
        if (h < 0f) h = 0f;
        return new Vector2(workWidth, h);
    }

    /// <summary>
    /// Top-left position of the central dockspace:
    /// <c>workPos + (0, toolbarHeight)</c>.
    /// </summary>
    public static Vector2 CentralPos(Vector2 workPos, float toolbarHeight)
    {
        return new Vector2(workPos.X, workPos.Y + toolbarHeight);
    }
}

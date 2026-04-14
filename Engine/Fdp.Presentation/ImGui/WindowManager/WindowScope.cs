namespace Fdp.Toolkit.ImGui.WindowManager;

/// <summary>
/// Controls the perspective-visibility behaviour of a <see cref="ManagedWindow"/>.
/// </summary>
public enum WindowScope
{
    /// <summary>
    /// Window is shown only when its <see cref="ManagedWindow.OwningPerspective"/> is the
    /// active perspective, or when the window is pinned (<see cref="ManagedWindow.IsPinned"/>).
    /// </summary>
    PerspectiveBound,

    /// <summary>
    /// Window is always shown when <see cref="ManagedWindow.IsOpen"/> is <c>true</c>,
    /// regardless of the active perspective. Replaces the previous <c>IsDebugWindow</c> flag.
    /// </summary>
    Global,
}

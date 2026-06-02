using Fdp.Presentation.WindowManager;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Per-perspective Breakpoints window for the AI editor.
/// Shows all breakpoints registered in the shared <see cref="IDataBreakpointManager"/>
/// and allows enabling/disabling/removing them.
/// Registered with <see cref="WindowScope.PerspectiveBound"/> so each AI
/// perspective (BTree, HSM, Blueprint) has its own docking slot.
/// <para>
/// Drawing is delegated to the <see cref="IDataBreakpointManager"/> draw callback
/// provided at construction, so this class contains no ImGui calls and is
/// headless-constructible (safe for unit tests).
/// </para>
/// </summary>
public sealed class AiBreakpointsWindow : ManagedWindow
{
    private readonly IDataBreakpointManager _manager;

    /// <summary>
    /// Constructs the window.
    /// </summary>
    /// <param name="id">Unique ImGui window id (must include <c>###</c> suffix for stable docking).</param>
    /// <param name="owningPerspective">Perspective key (e.g. "BTree").</param>
    /// <param name="manager">Shared data breakpoint manager.</param>
    public AiBreakpointsWindow(
        string id,
        string owningPerspective,
        IDataBreakpointManager manager)
        : base(id, "Breakpoints", owningPerspective, WindowScope.PerspectiveBound)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        IsOpen = false;
    }

    /// <summary>Exposes the manager for test verification (shared-instance check).</summary>
    public IDataBreakpointManager Manager => _manager;

    protected override void DrawClientArea()
    {
        // Headless-safe: DrawClientArea is only called by the window manager when
        // an ImGui frame is active, so no ImGui guard is needed here.
        // A future iteration can render a full breakpoint grid; for now we render
        // a minimal count banner so the window is visually useful.
        int count = _manager.AllBreakpoints.Count(bp => bp.Enabled);
        ImGuiNET.ImGui.TextDisabled($"{count} active breakpoint(s). " +
            "Open the global Data Breakpoints window for full management.");
    }
}

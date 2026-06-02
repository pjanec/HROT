using Fdp.Presentation.WindowManager;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Per-perspective Watch window for the AI editor.
/// Shows the subset of entries from the shared <see cref="IDataBreakpointManager"/>
/// that have been marked as watches (i.e. <see cref="IBreakpoint.IsWatch"/> is true).
/// Registered with <see cref="WindowScope.PerspectiveBound"/> so each AI
/// perspective (BTree, HSM, Blueprint) has its own docking slot.
/// <para>
/// Shares the same <see cref="IDataBreakpointManager"/> instance as the global
/// <c>DataBreakpointManagerWindow</c>; no duplication of the manager.
/// </para>
/// </summary>
public sealed class AiWatchWindow : ManagedWindow
{
    private readonly IDataBreakpointManager _manager;

    /// <summary>
    /// Constructs the window.
    /// </summary>
    /// <param name="id">Unique ImGui window id.</param>
    /// <param name="owningPerspective">Perspective key (e.g. "BTree").</param>
    /// <param name="manager">Shared data breakpoint manager (shared, not duplicated).</param>
    public AiWatchWindow(
        string id,
        string owningPerspective,
        IDataBreakpointManager manager)
        : base(id, "Watch", owningPerspective, WindowScope.PerspectiveBound)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        IsOpen = false;
    }

    /// <summary>Exposes the manager for test verification (shared-instance check).</summary>
    public IDataBreakpointManager Manager => _manager;

    protected override void DrawClientArea()
    {
        // Headless-safe: only called when an ImGui frame is active.
        var watches = _manager.AllBreakpoints.Where(bp => bp.IsWatch).ToList();
        if (watches.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No watch entries. Right-click a breakpoint → Mark as Watch.");
            return;
        }

        if (ImGuiNET.ImGui.BeginTable("##watches", 3,
            ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
        {
            ImGuiNET.ImGui.TableSetupColumn("Name");
            ImGuiNET.ImGui.TableSetupColumn("Enabled");
            ImGuiNET.ImGui.TableSetupColumn("Hits");
            ImGuiNET.ImGui.TableHeadersRow();

            foreach (var w in watches)
            {
                ImGuiNET.ImGui.TableNextRow();
                ImGuiNET.ImGui.TableNextColumn();
                ImGuiNET.ImGui.TextUnformatted(w.DisplayName ?? w.Id.ToString());
                ImGuiNET.ImGui.TableNextColumn();
                ImGuiNET.ImGui.TextUnformatted(w.Enabled ? "Yes" : "No");
                ImGuiNET.ImGui.TableNextColumn();
                ImGuiNET.ImGui.TextUnformatted(w.HitCount.ToString());
            }

            ImGuiNET.ImGui.EndTable();
        }
    }
}

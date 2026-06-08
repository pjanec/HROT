using ImGuiNET;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class DebugPanelWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;

    public override string Title => _session.IsPaused ? "Debug [PAUSED]" : "Debug";

    // Captured on each DrawUI call -- readable by tests without an ImGui context.
    public bool? LastRenderedPausedState          { get; private set; }
    public IReadOnlyList<Breakpoint>? LastRenderedBreakpoints { get; private set; }
    public string? LastStepActionInvoked          { get; private set; }

    public DebugPanelWindow(IBlueprintDebugSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override void DrawUI()
    {
        var paused      = _session.IsPaused;
        var breakpoints = _session.GetBreakpoints();

        LastRenderedPausedState  = paused;
        LastRenderedBreakpoints  = breakpoints;
        LastStepActionInvoked    = null;

        // ImGui rendering requires a live context; skip in headless / test environments.
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        // Shared step-control row (Continue / Step Over / Step Into / Step Out)
        DebugStepControls.Draw(_session, action => LastStepActionInvoked = action);

        if (!paused) return;

        ImGui.Separator();

        if (ImGui.BeginTable("##bpTable", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Node ID");
            ImGui.TableSetupColumn("Asset ID");
            ImGui.TableSetupColumn("Hits");
            ImGui.TableHeadersRow();

            foreach (var bp in breakpoints)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(bp.NodeId);
                ImGui.TableNextColumn();
                ImGui.Text(bp.AssetId.ToString("D"));
                ImGui.TableNextColumn();
                ImGui.Text(bp.HitCount.ToString());
            }

            ImGui.EndTable();
        }
    }

    public override void OnActivated()   { }
    public override void OnDeactivated() { }
}

using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using ImGuiNET;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — this window's own state, dumped.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⛔ No id of its own — see <see cref="CallstackWindowPanelViewModel"/>'s remarks; a declared
/// literal serves as both address and kind for this <c>BlueprintEditorWindowBase</c> singleton,
/// registered once by <c>BlueprintWindowRegistrar</c> under <c>"Debug Panel"</c>.</para>
/// </summary>
public sealed record DebugPanelWindowPanelViewModel(
    string PanelId,
    string PanelKind,
    bool   IsPaused,
    int    BreakpointCount) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

public sealed class DebugPanelWindow : BlueprintEditorWindowBase
{
    /// <summary>⭐ <c>U-obs-5</c> — THE ADDRESS/KIND. ⛔ A declared literal — see
    /// <see cref="CallstackWindow.PanelId"/>'s remarks.</summary>
    internal const string PanelId = "debug-panel";

    private readonly IBlueprintDebugSession _session;

    public override string Title => _session.IsPaused ? "Debug [PAUSED]" : "Debug";

    // Captured on each DrawUI call -- readable by tests without an ImGui context.
    public bool? LastRenderedPausedState          { get; private set; }
    public IReadOnlyList<Breakpoint>? LastRenderedBreakpoints { get; private set; }
    public string? LastStepActionInvoked          { get; private set; }

    public DebugPanelWindow(IBlueprintDebugSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(PanelId);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. ⛔⛔ No ImGui.</summary>
    private DebugPanelWindowPanelViewModel BuildAndPublish(bool paused, IReadOnlyList<Breakpoint> breakpoints)
    {
        var vm = new DebugPanelWindowPanelViewModel(PanelId, PanelId, paused, breakpoints.Count);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal DebugPanelWindowPanelViewModel SimulateDrawUI()
        => BuildAndPublish(_session.IsPaused, _session.GetBreakpoints());

    public override void DrawUI()
    {
        var paused      = _session.IsPaused;
        var breakpoints = _session.GetBreakpoints();

        LastRenderedPausedState  = paused;
        LastRenderedBreakpoints  = breakpoints;
        LastStepActionInvoked    = null;
        BuildAndPublish(paused, breakpoints);

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

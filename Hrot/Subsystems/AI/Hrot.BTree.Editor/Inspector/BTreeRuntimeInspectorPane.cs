using ImGuiNET;
using Hrot.BTree.Editor.Debug;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// BTree-specific pane that plugs into the shared Runtime Inspector window.
/// Displays kernel state fields from BehaviorTreeStateSnapshot when a debug session is active.
/// Implements IRuntimeInspectorPane; the window selects this pane when the active asset is a BTree.
/// </summary>
public sealed class BTreeRuntimeInspectorPane : IRuntimeInspectorPane
{
    private IBTreeDebugSession? _session;

    public AssetKind TargetKind => AssetKind.BTree;

    /// <summary>Sets the debug session used for snapshot reads.</summary>
    public void SetSession(IBTreeDebugSession? session) => _session = session;

    public void Draw()
    {
        var snapshot = _session?.GetCurrentStateSnapshot();
        if (snapshot is null)
        {
            ImGui.TextDisabled("No live BTree state.");
            return;
        }

        DrawHeader(snapshot);
        ImGui.Spacing();
        DrawStackPanel(snapshot);
        ImGui.Spacing();
        DrawRegistersPanel(snapshot);
        ImGui.Spacing();
        DrawAsyncPanel(snapshot);
    }

    private static void DrawHeader(BehaviorTreeStateSnapshot snap)
    {
        ImGui.TextUnformatted("BTree State");
        ImGui.Separator();

        string runLabel = snap.RunningElementId.HasValue
            ? snap.RunningElementId.Value.ToString("D")[..8] + "..."
            : "(idle)";

        ImGui.Text($"Running: {runLabel}");
        ImGui.Text($"Stack depth: {snap.StackPointer}");
        ImGui.Text($"Tree version: {snap.TreeVersion}");
    }

    private static void DrawStackPanel(BehaviorTreeStateSnapshot snap)
    {
        ImGui.TextDisabled("Stack:");
        for (int i = 0; i < snap.StackPointer && i < snap.StackElementIds.Count; i++)
        {
            string label = snap.StackElementIds[i] is { } id
                ? id.ToString("D")[..8] + "..."
                : $"[{snap.NodeIndexStack[i]}]";
            ImGui.Text($"  [{i}] {label}");
        }
    }

    private static void DrawRegistersPanel(BehaviorTreeStateSnapshot snap)
    {
        ImGui.TextDisabled("Local registers:");
        for (int i = 0; i < snap.LocalRegisters.Count; i++)
            ImGui.Text($"  r{i} = {snap.LocalRegisters[i]}");
    }

    private static void DrawAsyncPanel(BehaviorTreeStateSnapshot snap)
    {
        if (snap.AsyncHandles.Count == 0) return;
        ImGui.TextDisabled("Async handles:");
        for (int i = 0; i < snap.AsyncHandles.Count; i++)
            ImGui.Text($"  [{i}] 0x{snap.AsyncHandles[i]:X16}");
    }
}

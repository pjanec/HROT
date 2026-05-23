using System.Numerics;
using ImGuiNET;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Debug;

namespace Hrot.Hsm.Editor.Inspector;

/// <summary>
/// Runtime inspector pane for the HSM asset type.
/// Displayed in the debug panel when an HSM debug session is attached.
/// Renders the current instance snapshot: phase, active leaves, event queue,
/// timer slots, and history slots.
/// </summary>
public sealed class HsmRuntimeInspectorPane : IRuntimeInspectorPane
{
    private IHsmDebugSession? _session;

    public AssetKind TargetKind => AssetKind.Hsm;

    /// <summary>Attaches or detaches the debug session providing live data.</summary>
    public void SetSession(IHsmDebugSession? session) => _session = session;

    public void Draw()
    {
        var snapshot = _session?.GetCurrentStateSnapshot();
        if (snapshot is null)
        {
            ImGui.TextDisabled("No live HSM state.");
            return;
        }

        DrawHeader(snapshot);
        ImGui.Spacing();
        DrawActiveLeaves(snapshot);
        ImGui.Spacing();
        DrawEventQueue(snapshot);
        ImGui.Spacing();
        DrawTimerSlots(snapshot);
        ImGui.Spacing();
        DrawHistorySlots(snapshot);
    }

    private static void DrawHeader(HsmInstanceSnapshot s)
    {
        ImGui.TextUnformatted("HSM Instance");
        ImGui.Separator();
        ImGui.Text($"Phase:      {s.Phase}");
        ImGui.Text($"MicroStep:  {s.MicroStep}");
        ImGui.Text($"Generation: {s.Generation}");
        ImGui.Text($"Flags:      {s.Flags}");
        if (s.ConsecutiveClamps > 0)
        {
            ImGui.TextColored(
                new Vector4(1f, 0.5f, 0f, 1f),
                $"Clamps:     {s.ConsecutiveClamps}");
        }
    }

    private static void DrawActiveLeaves(HsmInstanceSnapshot s)
    {
        ImGui.TextDisabled("Active leaf states:");
        if (s.ActiveLeafStableIds.Count == 0)
        {
            ImGui.Text("  (none)");
            return;
        }
        foreach (var id in s.ActiveLeafStableIds)
        {
            // Display shortened GUID until names are wired in Slice 3.
            var shortened = id.ToString("D")[..8] + "...";
            ImGui.Text($"  {shortened}");
        }
    }

    private static void DrawEventQueue(HsmInstanceSnapshot s)
    {
        if (s.EventQueue.Count == 0) return;
        ImGui.TextDisabled("Event queue:");
        foreach (var e in s.EventQueue)
            ImGui.Text($"  [{e.QueuePosition}] {e.EventName}  ({e.Priority})");
    }

    private static void DrawTimerSlots(HsmInstanceSnapshot s)
    {
        if (s.TimerSlots.Count == 0) return;
        ImGui.TextDisabled("Timer slots:");
        foreach (var t in s.TimerSlots)
        {
            string owner = t.OwningStateStableId.HasValue
                ? t.OwningStateStableId.Value.ToString("D")[..8] + "..."
                : "(empty)";
            ImGui.Text($"  [{t.SlotIndex}] {owner}  rem={t.RemainingTicks:F2}");
        }
    }

    private static void DrawHistorySlots(HsmInstanceSnapshot s)
    {
        if (s.HistorySlots.Count == 0) return;
        ImGui.TextDisabled("History slots:");
        foreach (var h in s.HistorySlots)
        {
            string histType = h.IsDeepHistory ? "deep" : "shallow";
            ImGui.Text($"  [{h.SlotIndex}] {histType}");
        }
    }
}

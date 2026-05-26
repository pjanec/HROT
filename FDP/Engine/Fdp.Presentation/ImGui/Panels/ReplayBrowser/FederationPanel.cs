using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.ReplayBrowser.Federation;
using ImGuiNET;

namespace Fdp.Presentation.Panels.ReplayBrowser;

public enum ViewMode { SingleNode, Merged }

/// <summary>
/// ImGui panel for per-node replay federation controls.
/// Handles mode toggle, per-node time offsets, base wall-tick input, and
/// the local-entities provider dropdown (Merged View only).
/// DESIGN §8.2.
/// </summary>
public sealed class FederationPanel
{
    private readonly FederatedReplayManager _manager;

    public ViewMode ActiveMode { get; private set; } = ViewMode.SingleNode;
    public event Action<ViewMode>? OnViewModeChanged;

    // Computed: true when any node in manager.NodeOffsets has a non-zero value
    public bool HasNonZeroOffset => _manager.NodeOffsets.Values.Any(v => v != 0L);

    /// <summary>
    /// Disclaimer text shown in Merged View. Exposed as a constant for testing.
    /// </summary>
    internal static string MergedViewDisclaimerText =>
        "Note: Merged View scrub may stutter -- this is by design (offline synthesis).";

    public FederationPanel(FederatedReplayManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    public void SetMode(ViewMode mode)
    {
        if (_manager == null) return;  // defensive
        ActiveMode = mode;
        OnViewModeChanged?.Invoke(mode);
    }

    public void SetNodeOffset(int nodeId, long offsetTicks)
        => _manager.SetNodeOffset(nodeId, offsetTicks);

    public void SetBaseWallTicks(long ticks)
        => _manager.SetBaseWallTicks(ticks);

    public void SetLocalEntitiesProvider(int nodeId)
        => _manager.SetLocalEntitiesProvider(nodeId);

    public void DrawContent()
    {
        // Mode radio: Single-Node | Merged
        int modeInt = (int)ActiveMode;
        if (Gui.RadioButton("Single-Node", ref modeInt, 0))
            SetMode(ViewMode.SingleNode);
        Gui.SameLine();
        if (Gui.RadioButton("Merged View", ref modeInt, 1))
            SetMode(ViewMode.Merged);

        if (ActiveMode == ViewMode.Merged)
        {
            Gui.TextDisabled(MergedViewDisclaimerText);

            // Local-Entities Provider dropdown
            int currentProviderId = _manager.LocalEntitiesProviderNodeId;
            Gui.Text("Local-Entities Provider:");
            Gui.SameLine();
            string previewLabel = $"Node {currentProviderId}";
            if (Gui.BeginCombo("##lep", previewLabel))
            {
                foreach (int nodeId in _manager.Contexts.Keys.OrderBy(x => x))
                {
                    bool selected = nodeId == currentProviderId;
                    if (Gui.Selectable($"Node {nodeId}", selected))
                        _manager.SetLocalEntitiesProvider(nodeId);
                }
                Gui.EndCombo();
            }
        }

        // Global causality banner
        if (HasNonZeroOffset)
            Gui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f),
                "Causality may not hold -- non-zero offsets active");

        // Per-node offset rows
        foreach (var kvp in _manager.Contexts.OrderBy(x => x.Key))
        {
            int nodeId = kvp.Key;
            long currentOffset = _manager.NodeOffsets.TryGetValue(nodeId, out long off) ? off : 0L;
            Gui.Text($"Node {nodeId} offset:");
            Gui.SameLine();
            int offsetInt = (int)currentOffset;  // ImGui int input for display only
            Gui.SetNextItemWidth(120f);
            if (Gui.InputInt($"##offset_{nodeId}", ref offsetInt))
                _manager.SetNodeOffset(nodeId, offsetInt);
            if (currentOffset != 0L)
            {
                Gui.SameLine();
                Gui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f), "[!]");
                if (Gui.IsItemHovered())
                    Gui.SetTooltip($"Node {nodeId} has a non-zero time offset.");
            }
        }
    }
}

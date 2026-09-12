using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.ReplayBrowser.Federation;
using ImGuiNET;

namespace Fdp.Presentation.Panels.ReplayBrowser;

public enum ViewMode { SingleNode, Merged }

/// <summary>⭐ One node's offset row, projected for the dump.</summary>
public sealed record FederationNodeRowViewModel(int NodeId, long OffsetTicks);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="FederationPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠⚠ <b>No production host draws this panel.</b> 📐 Measured — <c>ReplayBrowserSubsystem</c>
/// constructs <c>_federationPanel</c> and wires its <c>OnViewModeChanged</c> event, but never calls
/// <see cref="FederationPanel.DrawContent"/> anywhere in the tree. ⇒ per the queue's caller-registers
/// rule there is no host to call <c>DeclareInstrumented</c>/<c>Register</c> from —
/// <see cref="FederationPanel.BuildViewModel"/> exists so the projection is ready the moment a host
/// draws it, but this panel is NOT wired into <c>PanelSnapshot</c> yet. Reported rather than silently
/// skipped, per the sweep's own rule.</para>
/// </summary>
public sealed record FederationPanelViewModel(
    string PanelId,
    string PanelKind,
    ViewMode ActiveMode,
    bool HasNonZeroOffset,
    int LocalEntitiesProviderNodeId,
    IReadOnlyList<FederationNodeRowViewModel> Nodes) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

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

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the manager's node offsets. No ImGui. ⚠ Not wired to
    /// any host yet — see the view-model's own remarks.</summary>
    public FederationPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        var nodes = _manager.Contexts.Keys.OrderBy(x => x)
            .Select(nodeId => new FederationNodeRowViewModel(
                nodeId, _manager.NodeOffsets.TryGetValue(nodeId, out long off) ? off : 0L))
            .ToList();
        return new FederationPanelViewModel(
            panelId, panelKind, ActiveMode, HasNonZeroOffset, _manager.LocalEntitiesProviderNodeId, nodes);
    }

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

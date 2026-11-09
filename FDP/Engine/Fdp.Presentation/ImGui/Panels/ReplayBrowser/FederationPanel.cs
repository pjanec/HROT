using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.ReplayBrowser.Federation;
using ImGuiNET;
using Fdp.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser;

public enum ViewMode { SingleNode, Merged }

/// <summary>⭐ One node's offset row, projected for the dump.</summary>
/// <remarks>⭐ G-7 S3 — <paramref name="Name"/> is the row's label (a role when the host knows it, else <c>Node N</c>); <paramref name="OffsetMs"/> is the slider's value. ⭐ S5 (I7) — <paramref name="LocalsShown"/>: the node's <i>Local entities shown from</i> checkbox.</remarks>
public sealed record FederationNodeRowViewModel(NodeId NodeId, long OffsetTicks, string Name = "", double OffsetMs = 0, bool LocalsShown = true);

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
/// the local-entities-shown checkboxes (Merged View only, ⭐ S5).
/// DESIGN §8.2.
/// </summary>
public sealed class FederationPanel
{
    private readonly FederatedReplayManager _manager;
    private readonly int[] _nodeIds;   // the loaded nodes, ascending — fixed for a manager, so a drawn frame allocates nothing for the row order

    /// <summary>⭐ G-7 S3 — the slider's range, ± ms (the sync contract allows 80 ms between nodes, SimTimeSyncIntegrationTests.cs:35-37).</summary>
    public const int OffsetRangeMs = 500;

    /// <summary>⭐ G-7 S3 — names a node's row (Q-B6: by role) — the editor passes its participants' names; default <c>Node N</c> (a recording's sidecar carries no role).</summary>
    public Func<int, string>? NodeName { get; set; }

    public string NameOf(int nodeId) => NodeName?.Invoke(nodeId) ?? ("Node " + nodeId);

    public ViewMode ActiveMode { get; private set; } = ViewMode.SingleNode;
    public event Action<ViewMode>? OnViewModeChanged;

    // Computed: true when any node in manager.NodeOffsets has a non-zero value
    public bool HasNonZeroOffset
    {
        get
        {
            foreach (var v in _manager.NodeOffsets.Values) if (v != 0L) return true;
            return false;
        }
    }

    /// <summary>
    /// Disclaimer text shown in Merged View. Exposed as a constant for testing.
    /// </summary>
    internal static string MergedViewDisclaimerText =>
        "Note: Merged View scrub may stutter -- this is by design (offline synthesis).";

    public FederationPanel(FederatedReplayManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _nodeIds = manager.Contexts.Keys.OrderBy(x => x).ToArray();
    }

    public void SetMode(ViewMode mode)
    {
        if (_manager == null) return;  // defensive
        ActiveMode = mode;
        OnViewModeChanged?.Invoke(mode);
    }

    public void SetNodeOffset(int nodeId, long offsetTicks)
        => _manager.SetNodeOffset(nodeId, offsetTicks);

    /// <summary>⭐ G-7 S3 (R-4183) — the node's manual offset in ms (0 when none was set).</summary>
    public double GetNodeOffsetMs(int nodeId)
        => _manager.NodeOffsets.TryGetValue(nodeId, out long ticks) ? (double)ticks / TimeSpan.TicksPerMillisecond : 0d;

    /// <summary>
    /// ⭐ G-7 S3 (R-4183) — the slider's write: the node's manual offset in ms, applied to that node's wall stamps. It is <see cref="FederatedReplayManager.SetNodeOffset"/> — which
    /// re-seeks every node at the current time (so the merged view reflects it) and raises <c>OnTimeChanged</c> (the Replay Browser and the editor's Review source re-merge).
    /// </summary>
    public void SetNodeOffsetMs(int nodeId, double offsetMs)
        => _manager.SetNodeOffset(nodeId, (long)Math.Round(offsetMs * TimeSpan.TicksPerMillisecond));

    public void SetBaseWallTicks(long ticks)
        => _manager.SetBaseWallTicks(ticks);

    /// <summary>⭐ G-7 S5 — a rail's measurement of mockup ③: the screen position of each <i>Local entities shown from</i> checkbox as last drawn (filled only while <see cref="CaptureLayout"/> is true).</summary>
    public readonly List<(int Node, bool On, float X, float Y)> LocalsRows = new();
    public bool CaptureLayout { get; set; }

    /// <summary>The PRIMARY node (the clock / timeline node — see <see cref="FederatedReplayManager.LocalEntitiesProviderNodeId"/>); ⭐ S5: no longer picks which locals show.</summary>
    public void SetLocalEntitiesProvider(int nodeId)
        => _manager.SetLocalEntitiesProvider(nodeId);

    /// <summary>⭐ G-7 S5 (I7) — the node's <i>Local entities shown from</i> checkbox (default on): whether its local (no network id) entities are in the merged world. Re-merges at once.</summary>
    public bool IsLocalEntitiesShown(int nodeId) => _manager.IsLocalEntitiesShown(nodeId);

    /// <summary>⭐ G-7 S5 (I7) — the checkbox's write: <see cref="FederatedReplayManager.SetLocalEntitiesShown"/> (raises <c>OnTimeChanged</c>, so both hosts re-merge).</summary>
    public void SetLocalEntitiesShown(int nodeId, bool shown) => _manager.SetLocalEntitiesShown(nodeId, shown);

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the manager's node offsets. No ImGui. ⚠ Not wired to
    /// any host yet — see the view-model's own remarks.</summary>
    public FederationPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        var nodes = _nodeIds
            .Select(nodeId => new FederationNodeRowViewModel(
                (Fdp.Core.NodeId)(nodeId), _manager.NodeOffsets.TryGetValue(nodeId, out long off) ? off : 0L, NameOf(nodeId), GetNodeOffsetMs(nodeId), _manager.IsLocalEntitiesShown(nodeId)))
            .ToList();
        return new FederationPanelViewModel(
            panelId, panelKind, ActiveMode, HasNonZeroOffset, _manager.LocalEntitiesProviderNodeId, nodes);
    }

    /// <summary>⭐ G-7 S5 follow-up — whether <see cref="DrawContent"/> draws the Single-Node / Merged radio (+ its disclaimer). The Replay Browser's window does; ⛔ the editor's Federation window does not — its Source menu's rows ARE that choice (ONE control per choice).</summary>
    public bool ShowModeRadio { get; set; } = true;

    /// <summary>⭐ G-7 S5 follow-up — whether <see cref="DrawContent"/> draws the <i>Local entities shown from</i> checkboxes. ⛔ The editor's window does not: its Source menu draws them through <see cref="DrawLocalEntities"/> (ONE component, one place).</summary>
    public bool ShowLocals { get; set; } = true;

    /// <summary>
    /// ⭐ G-7 S5 (I7, mockup ③) — <b>the ONE component</b> of <i>Local entities shown from (no network id)</i>: a header (when <paramref name="header"/>) and one checkbox per node (default every node's on). Drawn by the Replay Browser's
    /// window (via <see cref="DrawContent"/>) AND by the editor's Source menu. ⛔ SUPERSEDED: the single "Local-Entities Provider" dropdown. 0 B per drawn frame (the label is cached per node).
    /// </summary>
    public void DrawLocalEntities(bool header = true)
    {
        if (header) Gui.TextDisabled(LocalsHeader);
        LocalsRows.Clear();
        _localLabels ??= new string[_nodeIds.Length]; _localNames ??= new string[_nodeIds.Length];
        for (int i = 0; i < _nodeIds.Length; i++)
        {
            int nodeId = _nodeIds[i];
            bool on = _manager.IsLocalEntitiesShown(nodeId);
            var at = Gui.GetCursorScreenPos();
            string name = NameOf(nodeId);
            if (_localLabels[i] is null || !string.Equals(_localNames![i], name, StringComparison.Ordinal)) { _localNames![i] = name; _localLabels[i] = $"{name}##locals_{nodeId}"; }
            if (Gui.Checkbox(_localLabels[i], ref on))
                SetLocalEntitiesShown(nodeId, on);
            if (CaptureLayout) LocalsRows.Add((nodeId, on, at.X, at.Y));
        }
    }

    /// <summary>The header of <see cref="DrawLocalEntities"/>.</summary>
    public const string LocalsHeader = "Local entities shown from (no network id)";

    private string[]? _localLabels, _localNames;

    /// <summary>A rail's observation: whether the last <see cref="DrawContent"/> drew the Single-Node / Merged radio.</summary>
    public bool ModeRadioDrawn { get; private set; }

    public void DrawContent()
    {
        ModeRadioDrawn = ShowModeRadio;
        if (ShowModeRadio)
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
                if (ShowLocals) { DrawLocalEntities(); Gui.Separator(); }
            }
        }
        else if (ShowLocals && ActiveMode == ViewMode.Merged) { DrawLocalEntities(); Gui.Separator(); }

        // Global causality banner
        if (HasNonZeroOffset)
            NodeEditor.Core.Widgets.RowMarks.WarningLine("Causality may not hold -- non-zero offsets active");

        // ⭐ G-7 S3 (R-4183) — Offsets: ONE slider per node, in ms, default 0, named by role (Q-B6); live — every change re-seeks at the current time (SetNodeOffsetMs).
        //   The editor's Review draws THIS panel in its Source switch, so there is no second slider.
        Gui.TextDisabled("Offsets (ms) — shifts a node's wall stamps; 0 = none");
        for (int i = 0; i < _nodeIds.Length; i++)
        {
            int nodeId = _nodeIds[i];
            NodeEditor.Core.Widgets.FormGrid.BeginField("fed.rows", NameOf(nodeId));
            int ms = (int)Math.Round(GetNodeOffsetMs(nodeId));
            Gui.SetNextItemWidth(200f);
            if (Gui.SliderInt($"##offset_{nodeId}", ref ms, -OffsetRangeMs, OffsetRangeMs, "%d ms"))
                SetNodeOffsetMs(nodeId, ms);
            if (ms != 0)
            {
                Gui.SameLine();
                Gui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f), "[!]");
                if (Gui.IsItemHovered())
                    Gui.SetTooltip($"{NameOf(nodeId)} has a non-zero time offset.");
            }
            NodeEditor.Core.Widgets.FormGrid.EndField();
        }
    }
}

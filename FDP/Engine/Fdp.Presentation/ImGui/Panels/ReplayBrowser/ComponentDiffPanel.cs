using System;
using NodeEditor.Core.Widgets;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Utils.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using ImGuiNET;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>⭐ One visible diff row, flattened (not a tree — the tree structure is ImGui-only
/// nesting/indent, not information the model needs to duplicate). A group row has null
/// Old/NewValue; a leaf row carries both.</summary>
public sealed record ComponentDiffRowViewModel(string Name, bool IsGroup, string? OldValue, string? NewValue, string? ValueTypeKind);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="ComponentDiffPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ Built from
/// <see cref="ComponentDiffPanel.CollectVisibleNodes"/> — the SAME filter <see cref="DrawContent(System.Collections.Generic.IReadOnlyList{Fdp.Toolkit.ReplayBrowser.Diff.DiffNode})"/>
/// walks, already exposed as a testable helper before this conversion.
/// </summary>
public sealed record ComponentDiffPanelViewModel(
    string PanelId,
    string PanelKind,
    bool IgnoreEpsilon,
    bool HideUnchanged,
    IReadOnlyList<ComponentDiffRowViewModel> Rows) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Renders a hierarchical diff tree showing per-field changes between two consecutive
/// recording frames. Syntax-colors value types; detects entity handles and fires
/// <see cref="OnEntityLinkClicked"/> when the user clicks them.
/// </summary>
public sealed class ComponentDiffPanel
{
    private bool _ignoreEpsilon;
    private bool _hideUnchanged = true;
    private readonly HashSet<Type> _excludedTypes = new();
    private string _typeFilter = string.Empty;

    /// <summary>Current diff list displayed by <see cref="DrawContent()"/>.</summary>
    public IReadOnlyList<DiffNode> CurrentDiffs
    {
        get => _currentDiffs;
        set { _currentDiffs = value; _labels.Clear(); }
    }
    private IReadOnlyList<DiffNode> _currentDiffs = Array.Empty<DiffNode>();

    /// <summary>⭐ R-2 / V2 — each row's tree label (<c>name##hash</c>) built ONCE per diff, not per frame: a steady frame draws 0 B.</summary>
    private readonly Dictionary<DiffNode, string> _labels = new(ReferenceEqualityComparer.Instance);
    private string LabelOf(DiffNode n)
    {
        if (!_labels.TryGetValue(n, out var label)) _labels[n] = label = $"{n.Name}##{n.GetHashCode()}";
        return label;
    }

    /// <summary>
    /// Fired when the user clicks an entity-handle button inside the diff tree.
    /// </summary>
    public Action<Entity>? OnEntityLinkClicked { get; set; }
    public Action<int>? OnSeekToChangeRequested { get; set; }
    public bool IsSearching { get; set; }
    public bool IsEpsilonIgnored => _ignoreEpsilon;
    public IReadOnlySet<Type> ExcludedTypes => _excludedTypes;

    /// <summary>
    /// Optional query for whether the UI is in Merged View.
    /// When non-null and returning true, seek-to-change buttons are disabled.
    /// </summary>
    public Func<bool>? IsMergedViewQuery { get; set; }

    /// <summary>Tooltip shown on the seek-to-change buttons when Merged View is active.</summary>
    public const string MergedViewDisabledTooltip =
        "Step-change search is disabled in Merged View. Switch to Single-Node View to seek to the next change.";

    /// <summary>Test helper: returns true when seek-to-change should be enabled.</summary>
    internal static bool IsSeekToChangeEnabled(bool isSearching, bool isMerged)
        => !isSearching && !isMerged;

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure, flattened projection of the visible diff rows. No ImGui.
    /// ⭐ Reuses <see cref="CollectVisibleNodes"/>, exactly the set <see cref="DrawContent(System.Collections.Generic.IReadOnlyList{Fdp.Toolkit.ReplayBrowser.Diff.DiffNode})"/>
    /// visits.</summary>
    public ComponentDiffPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        var visible = CollectVisibleNodes(CurrentDiffs, _hideUnchanged);
        var rows = visible.Select(n => n is DiffValue v
            ? new ComponentDiffRowViewModel(v.Name, false, v.OldValue, v.NewValue, v.ValueType.ToString())
            : new ComponentDiffRowViewModel(n.Name, true, null, null, null))
            .ToList();
        return new ComponentDiffPanelViewModel(panelId, panelKind, _ignoreEpsilon, _hideUnchanged, rows);
    }

    // ── Draw entry point ──────────────────────────────────────────────────

    public void DrawContent()
        => DrawContent(CurrentDiffs);

    /// <summary>
    /// Renders <paramref name="diffs"/> using the current filter settings.
    /// </summary>
    private static readonly string[] DiffLabels = { "Ignore Epsilon (< 0.001)", "Hide Unchanged", "Compressed size", "Seek to change" };

    /// <summary>⭐ B-7 part 3 — whether a recording is loaded (the host binds the timeline panel's); drives the meta row below.</summary>
    public Func<bool>? HasRecording { get; set; }

    /// <summary>⭐ R-2 / V2 (docs/UX/DESIGN_Review_Perspective.md §4) — when the host returns text, the panel draws only that (<i>"Open an exercise recording (AAR) to diff frames"</i>). Null / returning null ⇒ as ever.</summary>
    public Func<string?>? UnavailableReason { get; set; }

    /// <summary>
    /// ⭐ B-7 part 3 (R-4155) — the per-frame debug row that used to sit empty in the retired Replay Timeline window (<c>Tick · SimFrame · SimTime · FrameType · CompressedSize</c>;
    /// still a placeholder: the values are not read from the recording yet). It belongs with the frame diff, which is per-frame debug.
    /// </summary>
    private void DrawMetaRows()
    {
        bool rec = HasRecording?.Invoke() ?? false;
        FormGrid.Row("Tick");             Gui.TextDisabled("-");
        FormGrid.Row("Sim frame");        Gui.TextDisabled("-");
        FormGrid.Row("Sim time");         Gui.TextDisabled("-");
        FormGrid.Row(rec ? "Frame type" : "Type"); Gui.TextDisabled("-");
        FormGrid.Row(rec ? "Compressed size" : "Size"); Gui.TextDisabled("-");
    }

    private void DrawTypeFilter()
    {
        if (Gui.Button("Filter…"))
            Gui.OpenPopup("##diff_filter_popup");

        if (Gui.BeginPopup("##diff_filter_popup"))
        {
            Gui.SetNextItemWidth(250f);
            Gui.InputTextWithHint("##diff_type_filter", "Filter components...", ref _typeFilter, 128);
            Gui.Separator();

            if (Gui.Button("Select All"))
                _excludedTypes.Clear();
            Gui.SameLine();
            if (Gui.Button("Deselect All"))
            {
                foreach (var t in ComponentTypeRegistry.GetAllRegistered())
                    _excludedTypes.Add(t);
            }
            Gui.Separator();

            Gui.BeginChild("##diff_types_scroll", new Vector2(0, 300), ImGuiChildFlags.None);
            foreach (var t in ComponentTypeRegistry.GetAllRegistered().OrderBy(x => x.Name))
            {
                if (!string.IsNullOrEmpty(_typeFilter)
                    && t.Name.IndexOf(_typeFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool isChecked = !_excludedTypes.Contains(t);
                if (Gui.Checkbox(t.Name, ref isChecked))
                {
                    if (isChecked) _excludedTypes.Remove(t);
                    else _excludedTypes.Add(t);
                }
            }
            Gui.EndChild();
            Gui.EndPopup();
        }

        if (!Gui.IsPopupOpen("##diff_filter_popup"))
            _typeFilter = string.Empty;

    }

    private void DrawSeekButtons(bool isMerged, bool prevNextEnabled)
    {
        TransportIconRenderer.DrawButton("##prev_change", 20f, TransportShape.StepBack, prevNextEnabled, out _, out bool prevClicked);
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
        {
            Gui.SetTooltip(isMerged
                ? MergedViewDisabledTooltip
                : "Seek to previous frame with changes");
        }
        if (prevClicked && prevNextEnabled)
            OnSeekToChangeRequested?.Invoke(-1);

        Gui.SameLine();
        TransportIconRenderer.DrawButton("##next_change", 20f, TransportShape.StepFwd, prevNextEnabled, out _, out bool nextClicked);
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
        {
            Gui.SetTooltip(isMerged
                ? MergedViewDisabledTooltip
                : "Seek to next frame with changes");
        }
        if (nextClicked && prevNextEnabled)
            OnSeekToChangeRequested?.Invoke(1);

    }

    public void DrawContent(IReadOnlyList<DiffNode> diffs)
    {
        if (UnavailableReason?.Invoke() is { } unavailable) { Gui.TextDisabled(unavailable); return; }
        bool isMerged = IsMergedViewQuery?.Invoke() ?? false;
        bool prevNextEnabled = !IsSearching && !isMerged;
        // ⭐ CE-4289 ⑧ — label-first like every form (E17 §3): the per-frame meta, the options, the component filter and the seek-to-change buttons are ROWS of one grid (label | control)
        if (FormGrid.Begin("diff.options", DiffLabels))
        {
            DrawMetaRows();
            FormGrid.Row("Ignore Epsilon (< 0.001)"); Gui.Checkbox("##ignoreepsilon", ref _ignoreEpsilon);
            FormGrid.Row("Hide Unchanged");           Gui.Checkbox("##hideunchanged", ref _hideUnchanged);
            FormGrid.Row("Components");
            DrawTypeFilter();
            FormGrid.Row("Seek to change");
            DrawSeekButtons(isMerged, prevNextEnabled);
            FormGrid.End();
        }

        Gui.Separator();

        if (diffs.Count == 0)
        {
            Gui.TextDisabled("No component modifications detected in this frame.");
            return;
        }

        var tableFlags = ImGuiTableFlags.Borders
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.Resizable
            | ImGuiTableFlags.SizingFixedFit;

        if (!Gui.BeginTable("DiffViewerTable", 2, tableFlags))
            return;

        Gui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200f);
        Gui.TableSetupColumn("Value Transition", ImGuiTableColumnFlags.WidthStretch);
        Gui.TableHeadersRow();

        for (int i = 0; i < diffs.Count; i++)   // ⭐ R-2 / V2 — indexed: a foreach over the interface boxes its enumerator (a steady frame draws 0 B)
            DrawDiffNode(diffs[i]);

        Gui.EndTable();
    }

    // ── Node traversal ────────────────────────────────────────────────────

    private void DrawDiffNode(DiffNode node)
    {
        // Early-return cull: prune unchanged branches when the toggle is active.
        if (_hideUnchanged && !node.IsModified)
            return;

        Gui.TableNextRow();
        Gui.TableSetColumnIndex(0);

        if (node is DiffObject group)
        {
            bool isOpen = Gui.TreeNodeEx(
                LabelOf(group),
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

            if (isOpen)
            {
                var children = group.Children;
                for (int i = 0; i < children.Count; i++)
                    DrawDiffNode(children[i]);
                Gui.TreePop();
            }
        }
        else if (node is DiffValue val)
        {
            Gui.TreeNodeEx(
                LabelOf(val),
                ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

            Gui.TableSetColumnIndex(1);
            RenderValueTransition(val);
        }
    }

    private void RenderValueTransition(DiffValue val)
    {
        // Entity-handle detection for both sides
        bool oldIsEntity = ImGuiEntityLink.TryParse(val.OldValue, out Entity oldEntity);
        bool newIsEntity = ImGuiEntityLink.TryParse(val.NewValue, out Entity newEntity);

        if (oldIsEntity || newIsEntity)
        {
            // Render old value
            if (oldIsEntity)
                ImGuiEntityLink.Draw(val.OldValue);
            else
                Gui.TextDisabled(val.OldValue);

            Gui.SameLine();
            Gui.TextUnformatted(" -> ");
            Gui.SameLine();

            // Render new value; fire callback on click
            if (newIsEntity)
            {
                if (ImGuiEntityLink.Draw(val.NewValue))
                    OnEntityLinkClicked?.Invoke(newEntity);
            }
            else
            {
                RenderSyntaxColoredValue(val.NewValue, val.ValueType);
            }
        }
        else
        {
            // Standard syntax-colored transition
            Gui.TextDisabled(val.OldValue);
            Gui.SameLine();
            Gui.TextUnformatted(" -> ");
            Gui.SameLine();
            RenderSyntaxColoredValue(val.NewValue, val.ValueType);
        }
    }

    private static void RenderSyntaxColoredValue(string value, JsonValueKind kind)
    {
        Vector4 color = kind switch
        {
            JsonValueKind.Number => new Vector4(0.30f, 0.80f, 1.00f, 1f), // cyan
            JsonValueKind.String => new Vector4(0.40f, 1.00f, 0.40f, 1f), // green
            JsonValueKind.True   => new Vector4(0.90f, 0.60f, 0.20f, 1f), // amber
            JsonValueKind.False  => new Vector4(0.90f, 0.60f, 0.20f, 1f), // amber
            _                    => new Vector4(0.85f, 0.85f, 0.85f, 1f), // light gray
        };
        Gui.TextColored(color, value);
    }

    // ── Testable helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the subset of <paramref name="diffs"/> that would be visited by the
    /// renderer given <paramref name="hideUnchanged"/>.  Does not call any ImGui API.
    /// </summary>
    public static IReadOnlyList<DiffNode> CollectVisibleNodes(
        IReadOnlyList<DiffNode> diffs,
        bool hideUnchanged)
    {
        var result = new List<DiffNode>();
        foreach (var root in diffs)
            CollectNode(root, hideUnchanged, result);
        return result;
    }

    private static void CollectNode(DiffNode node, bool hideUnchanged, List<DiffNode> result)
    {
        if (hideUnchanged && !node.IsModified)
            return;

        result.Add(node);

        if (node is DiffObject group)
        {
            foreach (var child in group.Children)
                CollectNode(child, hideUnchanged, result);
        }
    }

    /// <summary>
    /// Test seam: fires <paramref name="callback"/> if <paramref name="node"/> is a
    /// DiffValue whose NewValue parses as an entity handle.
    /// </summary>
    internal static bool TryFireEntityLink(DiffValue node, Action<Entity> callback)
    {
        if (!ImGuiEntityLink.TryParse(node.NewValue, out Entity entity))
            return false;
        callback(entity);
        return true;
    }
}

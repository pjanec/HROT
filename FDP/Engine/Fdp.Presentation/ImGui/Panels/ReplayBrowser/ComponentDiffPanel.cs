using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Utils.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using ImGuiNET;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>
/// Renders a hierarchical diff tree showing per-field changes between two consecutive
/// recording frames. Syntax-colors value types; detects entity handles and fires
/// <see cref="OnEntityLinkClicked"/> when the user clicks them.
/// </summary>
public sealed class ComponentDiffPanel
{
    private bool _ignoreEpsilon;
    private bool _hideUnchanged = true;

    /// <summary>Current diff list displayed by <see cref="DrawContent()"/>.</summary>
    public IReadOnlyList<DiffNode> CurrentDiffs { get; set; }
        = Array.Empty<DiffNode>();

    /// <summary>
    /// Fired when the user clicks an entity-handle button inside the diff tree.
    /// </summary>
    public Action<Entity>? OnEntityLinkClicked { get; set; }
    public Action<int>? OnSeekToChangeRequested { get; set; }
    public bool IsSearching { get; set; }

    // ── Draw entry point ──────────────────────────────────────────────────

    public void DrawContent()
        => DrawContent(CurrentDiffs);

    /// <summary>
    /// Renders <paramref name="diffs"/> using the current filter settings.
    /// </summary>
    public void DrawContent(IReadOnlyList<DiffNode> diffs)
    {
        Gui.Checkbox("Ignore Epsilon (< 0.001)", ref _ignoreEpsilon);
        Gui.SameLine();
        Gui.Checkbox("Hide Unchanged Components & Fields", ref _hideUnchanged);

        Gui.SameLine();

        TransportIconRenderer.DrawButton("##prev_change", 20f, TransportShape.StepBack, !IsSearching, out _, out bool prevClicked);
        if (prevClicked && !IsSearching)
            OnSeekToChangeRequested?.Invoke(-1);

        Gui.SameLine();
        TransportIconRenderer.DrawButton("##next_change", 20f, TransportShape.StepFwd, !IsSearching, out _, out bool nextClicked);
        if (nextClicked && !IsSearching)
            OnSeekToChangeRequested?.Invoke(1);

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

        foreach (var root in diffs)
            DrawDiffNode(root);

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
                $"{group.Name}##{group.GetHashCode()}",
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

            if (isOpen)
            {
                foreach (var child in group.Children)
                    DrawDiffNode(child);
                Gui.TreePop();
            }
        }
        else if (node is DiffValue val)
        {
            Gui.TreeNodeEx(
                $"{val.Name}##{val.GetHashCode()}",
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

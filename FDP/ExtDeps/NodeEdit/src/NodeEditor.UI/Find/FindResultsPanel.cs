using System.Numerics;
using ImGuiNET;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Find;

/// <summary>
/// Side panel showing find results across an asset or project scope.
/// Groups results by graph, with collapsible sections.
/// </summary>
public sealed class FindResultsPanel
{
    private readonly System.Action<GraphId, NodeId?> _navigateTo;
    private readonly Dictionary<string, bool>        _collapsed = new();

    /// <summary>Current set of results to display.</summary>
    public IReadOnlyList<FindResult> Results { get; set; } = Array.Empty<FindResult>();

    /// <summary>Whether the panel is visible.</summary>
    public bool IsVisible { get; set; }

    /// <summary>
    /// Create a find-results panel.
    /// </summary>
    /// <param name="navigateTo">Callback invoked when the user clicks a result to navigate there.</param>
    public FindResultsPanel(System.Action<GraphId, NodeId?> navigateTo)
    {
        _navigateTo = navigateTo;
    }

    /// <summary>Draw the panel contents inside the current ImGui window.</summary>
    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.Text($"Find Results  ({Results.Count})");
        ImGui.Separator();

        if (Results.Count == 0)
        {
            ImGui.TextDisabled("No results.");
            return;
        }

        // Group by graph: null graph ID uses a special "current" bucket
        const string NullKey = "\x01__null__";
        var byGraph = new Dictionary<string, (GraphId? Id, List<FindResult> Items)>();
        foreach (var r in Results)
        {
            var key = r.Graph.HasValue ? r.Graph.Value.ToString() : NullKey;
            if (!byGraph.TryGetValue(key, out var entry))
            {
                entry = (r.Graph, new List<FindResult>());
                byGraph[key] = entry;
            }
            entry.Items.Add(r);
        }

        foreach (var (key, (graphId, group)) in byGraph)
        {
            var header = graphId.HasValue ? graphId.Value.ToString() : "Current Graph";
            if (!_collapsed.TryGetValue(key, out _)) _collapsed[key] = false;

            if (ImGui.CollapsingHeader($"{header} ({group.Count})##g_{key}"))
            {
                _collapsed[key] = false;
                DrawGroup(group, graphId);
            }
            else
            {
                _collapsed[key] = true;
            }
        }
    }

    // ── private ──────────────────────────────────────────────────────────────

    private void DrawGroup(List<FindResult> group, GraphId? graphId)
    {
        ImGui.Indent(8f);
        foreach (var result in group)
        {
            var icon = result.Kind switch
            {
                FindResultKind.Node     => "[N]",
                FindResultKind.Pin      => "[P]",
                FindResultKind.Comment  => "[C]",
                FindResultKind.Variable => "[V]",
                FindResultKind.Function => "[F]",
                _                       => "[ ]",
            };
            bool clicked = ImGui.Selectable($"{icon} {result.DisplayLabel}##r_{result.DisplayLabel}_{result.Node}");
            if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(result.MatchSnippet))
                ImGui.SetTooltip(result.MatchSnippet);
            if (clicked)
            {
                if (graphId.HasValue) _navigateTo(graphId.Value, result.Node);
            }
        }
        ImGui.Unindent(8f);
    }
}

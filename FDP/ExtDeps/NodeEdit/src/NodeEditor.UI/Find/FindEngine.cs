using System.Text.RegularExpressions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Search;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Find;

/// <summary>
/// Pure search logic: parses a <see cref="FindQuery"/> and enumerates matching
/// entities within a graph model.
/// </summary>
public sealed class FindEngine
{
    private readonly IGraphModel           _model;
    private readonly IGraphSearchProvider? _extras;

    /// <summary>Create a new engine for the specified model and optional extra-text provider.</summary>
    public FindEngine(IGraphModel model, IGraphSearchProvider? extras)
    {
        _model  = model;
        _extras = extras;
    }

    /// <summary>
    /// Execute a search and return all matching results in ranked order.
    /// </summary>
    /// <param name="query">Parsed query from <see cref="FindQueryParser"/>.</param>
    /// <param name="scope">Which graphs to search (only <see cref="FindScope.CurrentGraph"/> is handled here).</param>
    /// <param name="view">The active graph view (used for state-based prefix filters).</param>
    public IEnumerable<FindResult> Search(FindQuery query, FindScope scope, GraphView view)
    {
        // Build the result list in score order.
        var results = new List<(int Score, FindResult Result)>();

        foreach (var node in _model.Nodes)
        {
            // ── Prefix filter checks ──────────────────────────────────────
            if (query.Prefixes.TryGetValue("kind", out var kindFilter) &&
                !node.Kind.Id.Contains(kindFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (query.Prefixes.TryGetValue("category", out var catFilter) &&
                !node.Category.ToString().Contains(catFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (query.Prefixes.TryGetValue("error", out _) &&
                (node.State & NodeState.Error) == 0)
                continue;

            if (query.Prefixes.TryGetValue("warning", out _) &&
                (node.State & NodeState.Warning) == 0)
                continue;

            // breakpoint and watched: not modeled in current NodeState — skip prefix silently

            // ── Type prefix: search pins ──────────────────────────────────
            if (query.Prefixes.TryGetValue("type", out var typeFilter))
            {
                foreach (var pin in node.Pins)
                {
                    if (pin.Type?.Id.Contains(typeFilter, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var label = $"{node.Title} [{pin.Label}]";
                        results.Add((500, new FindResult(
                            FindResultKind.Pin,
                            _model.Id, node.Id, pin.Id, null,
                            label, $"type: {pin.Type!.Value.Id}",
                            Array.Empty<int>())));
                    }
                }
                continue; // type-prefix only matches pins
            }

            // ── Free-text match ───────────────────────────────────────────
            if (!string.IsNullOrEmpty(query.FreeText))
            {
                var searchable = BuildSearchableText(node);
                var (score, positions) = MatchText(query.FreeText, searchable);
                if (score <= 0) continue;

                results.Add((score, new FindResult(
                    FindResultKind.Node,
                    _model.Id, node.Id, null, null,
                    node.Title, searchable,
                    positions)));
            }
            else
            {
                // Empty free text + no unmatched prefix = match everything
                results.Add((1, new FindResult(
                    FindResultKind.Node,
                    _model.Id, node.Id, null, null,
                    node.Title, node.Title,
                    Array.Empty<int>())));
            }
        }

        // Comments
        if (!query.Prefixes.ContainsKey("type") && !query.Prefixes.ContainsKey("kind"))
        {
            foreach (var comment in _model.Comments)
            {
                if (!string.IsNullOrEmpty(query.FreeText))
                {
                    var (score, positions) = MatchText(query.FreeText, comment.Text);
                    if (score <= 0) continue;
                    results.Add((score, new FindResult(
                        FindResultKind.Comment,
                        _model.Id, null, null, comment.Id,
                        comment.Text, comment.Text,
                        positions)));
                }
                else
                {
                    results.Add((1, new FindResult(
                        FindResultKind.Comment,
                        _model.Id, null, null, comment.Id,
                        comment.Text, comment.Text,
                        Array.Empty<int>())));
                }
            }
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results.Select(r => r.Result);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string BuildSearchableText(INodeModel node)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(node.Title);
        if (!string.IsNullOrEmpty(node.Subtitle))  { sb.Append(' '); sb.Append(node.Subtitle); }
        sb.Append(' '); sb.Append(node.Category);
        foreach (var pin in node.Pins)
        {
            sb.Append(' '); sb.Append(pin.Label);
            if (pin.Default?.Value is { } val)
            {
                sb.Append(' '); sb.Append(val);
            }
        }
        if (_extras is not null)
        {
            var extra = _extras.GetSearchableText(node);
            if (!string.IsNullOrWhiteSpace(extra)) { sb.Append(' '); sb.Append(extra); }
        }
        return sb.ToString();
    }

    private static (int Score, IReadOnlyList<int> Positions) MatchText(string query, string text)
    {
        var result = FuzzyMatcher.Score(query, text);
        return result.HasMatch ? (result.Score, result.MatchPositions) : (0, Array.Empty<int>());
    }
}

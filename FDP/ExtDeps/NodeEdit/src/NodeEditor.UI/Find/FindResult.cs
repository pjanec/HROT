using NodeEditor.Primitives;

namespace NodeEditor.UI.Find;

/// <summary>The kind of entity that matched a find query.</summary>
public enum FindResultKind
{
    /// <summary>A node matched.</summary>
    Node,
    /// <summary>A pin label matched.</summary>
    Pin,
    /// <summary>A pin default value matched.</summary>
    PinDefault,
    /// <summary>A comment box matched.</summary>
    Comment,
    /// <summary>A variable reference matched.</summary>
    Variable,
    /// <summary>A function call site matched.</summary>
    Function,
}

/// <summary>
/// A single result from a find operation.
/// </summary>
/// <param name="Kind">The kind of entity that matched.</param>
/// <param name="Graph">The graph that contains the match (may be null for current-graph scope).</param>
/// <param name="Node">The node that matched, or the node owning the matched pin/default.</param>
/// <param name="Pin">The pin that matched (null for node/comment matches).</param>
/// <param name="Comment">The comment that matched (null unless Kind == Comment).</param>
/// <param name="DisplayLabel">Human-readable label shown in the results list.</param>
/// <param name="MatchSnippet">Short context snippet around the match.</param>
/// <param name="MatchPositions">Character positions within <see cref="DisplayLabel"/> that were matched.</param>
public sealed record FindResult(
    FindResultKind Kind,
    GraphId?       Graph,
    NodeId?        Node,
    PinId?         Pin,
    CommentId?     Comment,
    string         DisplayLabel,
    string         MatchSnippet,
    IReadOnlyList<int> MatchPositions);

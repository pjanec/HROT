using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Read-only view of a single node. Implemented by host.
/// </summary>
public interface INodeModel
{
    /// <summary>Stable id.</summary>
    NodeId Id { get; }

    /// <summary>The kind of node, used by catalog lookups and Details panel routing.</summary>
    NodeKindKey Kind { get; }

    /// <summary>Display title shown in the header.</summary>
    string Title { get; }

    /// <summary>Optional subtitle line under the title.</summary>
    string? Subtitle { get; }

    /// <summary>Coarse category, drives header color and icon.</summary>
    NodeCategory Category { get; }

    /// <summary>Canvas position of the node's top-left corner.</summary>
    Vector2 Position { get; }

    /// <summary>Explicit size override; if null, the editor auto-sizes based on content.</summary>
    Vector2? SizeOverride { get; }

    /// <summary>Bit flags for current state (disabled, executing, …).</summary>
    NodeState State { get; }

    /// <summary>Tooltip shown when hovering the node's status icons.</summary>
    string? StatusTooltip { get; }

    /// <summary>Whether the node is rendered collapsed.</summary>
    bool IsCollapsed { get; }

    /// <summary>Whether advanced pins are shown (otherwise hidden behind disclosure).</summary>
    bool ShowAdvancedPins { get; }

    /// <summary>The node's pins in declaration order.</summary>
    IReadOnlyList<IPinModel> Pins { get; }

    /// <summary>
    /// Parent container id, or null if this node is at the root level.
    /// Nodes inside a container store Position in the container's interior coordinate space.
    /// Nodes at root level store Position in canvas-absolute coordinates.
    /// Default: null (root level). Override only when the node is a container child.
    /// </summary>
    NodeId? ParentContainerId => null;

    /// <summary>
    /// Optional short glyph drawn in the header's top-left corner (e.g. the italic <c>ƒ</c> that
    /// marks a function-call node, Unreal-style). Null (default) draws no glyph. Kept as a plain
    /// string so the host owns the vocabulary; the renderer just draws it with the node font.
    /// </summary>
    string? HeaderGlyph => null;
}

using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// What the cursor is currently over. Computed every frame by the canvas renderer
/// during hit-testing and consumed by event-handling code.
/// Mutually exclusive: only one of the IDs is non-empty.
/// </summary>
public readonly record struct HoverInfo
{
    public HoverKind Kind { get; init; }
    public NodeId Node { get; init; }
    public PinId Pin { get; init; }
    public LinkId Link { get; init; }
    public CommentId Comment { get; init; }
    public RerouteRef Reroute { get; init; }
    /// <summary>For comments: whether the cursor is on the title bar (drag), the body, or a resize handle.</summary>
    public CommentHoverZone CommentZone { get; init; }

    public static HoverInfo None => default;
}

public enum HoverKind { None, Node, Pin, Link, Comment, Reroute }

public enum CommentHoverZone { None, Header, Body, ResizeHandle }

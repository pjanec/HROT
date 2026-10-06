using NodeEditor.Core.Interfaces;
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
    public AttachmentId Attachment { get; init; }
    /// <summary>For custom elements: the renderer and element key that was hit.</summary>
    public CustomElementRef CustomElement { get; init; }
    /// <summary>For comments: whether the cursor is on the title bar (drag), the body, or a resize handle.</summary>
    public CommentHoverZone CommentZone { get; init; }
    /// <summary>Index of the active comment resize handle (0-7).</summary>
    public int CommentResizeHandle { get; init; }
    /// <summary>For containers: which zone of the container the cursor is over.</summary>
    public ContainerHoverZone ContainerZone { get; init; }
    /// <summary>CE-1004: for <see cref="ContainerHoverZone.RegionDivider"/>, the band ABOVE (or left of) the divider.</summary>
    public int ContainerRegionIndex { get; init; }
    /// <summary>CE-1004: for <see cref="ContainerHoverZone.ResizeEdge"/>, which edge(s) the cursor is on.</summary>
    public ContainerResizeEdge ContainerEdge { get; init; }

    public static HoverInfo None => default;
}

/// <summary>What the cursor is over. <see cref="NodeEdge"/> (CE-1000) is the link-start band just inside a node's border, used only by <c>LinkRouting.NodeToNode</c> graphs.</summary>
public enum HoverKind { None, Node, Pin, Link, Comment, Reroute, Attachment, Container, CustomElement, NodeEdge }

public enum CommentHoverZone { None, Header, Body, ResizeHandle }

/// <summary>Zone of a container node that the cursor is over.</summary>
public enum ContainerHoverZone { None, Header, CollapseArrow, Interior, RegionDivider, ResizeEdge }

/// <summary>CE-1004: the container edge(s) a resize drag moves.</summary>
[System.Flags]
public enum ContainerResizeEdge { None = 0, Right = 1, Bottom = 2, Corner = Right | Bottom }

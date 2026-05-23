using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Commands;

/// <summary>
/// Base class for all editor-initiated mutations. Discriminated by sealed
/// records. The host's <c>IGraphCommandSink</c> pattern-matches on these
/// to apply to its data store.
/// </summary>
public abstract record GraphCommand
{
    /// <summary>Move a set of nodes to new positions. One Batch entry per drag gesture.</summary>
    public sealed record MoveNodes(IReadOnlyList<NodeMove> Moves) : GraphCommand;

    /// <summary>Create a new node.</summary>
    public sealed record AddNode(
        NodeId AssignedId,
        NodeKindKey Kind,
        Vector2 Position,
        IReadOnlyDictionary<string, object?>? InitialProperties) : GraphCommand;

    /// <summary>Remove nodes (and their incident links). Host handles link cleanup.</summary>
    public sealed record RemoveNodes(IReadOnlyList<NodeId> Nodes) : GraphCommand;

    /// <summary>Create a new link between two pins.</summary>
    public sealed record AddLink(LinkId AssignedId, PinId From, PinId To) : GraphCommand;

    /// <summary>Remove specific links by id.</summary>
    public sealed record RemoveLinks(IReadOnlyList<LinkId> Links) : GraphCommand;

    /// <summary>Re-point one end of an existing link to a new pin.</summary>
    public sealed record ReplaceLinkEndpoint(
        LinkId Link,
        LinkEndpoint Endpoint,
        PinId NewPin) : GraphCommand;

    /// <summary>Set the default value on an input data pin.</summary>
    public sealed record SetPinDefault(PinId Pin, object? NewValue) : GraphCommand;

    /// <summary>Set a host-defined property on a node (used by Details panel).</summary>
    public sealed record SetNodeProperty(NodeId Node, string Key, object? Value) : GraphCommand;

    /// <summary>Toggle node collapsed state.</summary>
    public sealed record SetNodeCollapsed(NodeId Node, bool Collapsed) : GraphCommand;

    /// <summary>Toggle advanced-pins shown state on a node.</summary>
    public sealed record SetNodeAdvancedShown(NodeId Node, bool Shown) : GraphCommand;

    /// <summary>Toggle whether a node is disabled.</summary>
    public sealed record SetNodeDisabled(NodeId Node, bool Disabled) : GraphCommand;

    /// <summary>Add a comment box.</summary>
    public sealed record AddComment(
        CommentId AssignedId,
        string Text,
        Vector2 Position,
        Vector2 Size,
        Vector4 Color,
        bool MoveWithContents) : GraphCommand;

    /// <summary>Update one or more properties of a comment.</summary>
    public sealed record UpdateComment(
        CommentId Id,
        string? Text,
        Vector2? Position,
        Vector2? Size,
        Vector4? Color,
        int? ZOrder,
        bool? MoveWithContents) : GraphCommand;

    /// <summary>Remove a comment.</summary>
    public sealed record RemoveComment(CommentId Id) : GraphCommand;

    /// <summary>Insert a reroute waypoint into a link at the given canvas position.</summary>
    public sealed record InsertReroute(LinkId Link, Vector2 Position) : GraphCommand;

    /// <summary>Move an existing reroute waypoint.</summary>
    public sealed record MoveReroute(LinkId Link, int WaypointIndex, Vector2 NewPosition) : GraphCommand;

    /// <summary>Remove a reroute waypoint.</summary>
    public sealed record RemoveReroute(LinkId Link, int WaypointIndex) : GraphCommand;

    /// <summary>Promote a pin's current default value to a variable.</summary>
    public sealed record PromoteToVariable(
        PinId Pin,
        string VariableName,
        bool IsLocal,
        string? CategoryPath) : GraphCommand;

    /// <summary>Refactor: collapse a selection of nodes into a function call.</summary>
    public sealed record CollapseToFunction(
        IReadOnlyList<NodeId> Nodes,
        string FunctionName,
        bool Pure,
        string? CategoryPath) : GraphCommand;

    /// <summary>Refactor: collapse a selection of nodes into a macro call.</summary>
    public sealed record CollapseToMacro(
        IReadOnlyList<NodeId> Nodes,
        string MacroName,
        string? CategoryPath) : GraphCommand;

    /// <summary>Refactor: collapse a selection of nodes inside a comment box.</summary>
    public sealed record CollapseToComment(
        IReadOnlyList<NodeId> Nodes,
        string CommentText) : GraphCommand;

    /// <summary>Refactor: expand a function/macro call node, inlining its body.</summary>
    public sealed record ExpandNode(NodeId Node) : GraphCommand;

    /// <summary>Add an attachment to a host node.</summary>
    public sealed record AddAttachment(
        AttachmentId NewId,
        NodeId HostNodeId,
        AttachmentCategory Category,
        string? Glyph,
        string? Label,
        string? Tooltip,
        int StackIndex,
        IReadOnlyDictionary<string, object?>? HostProperties) : GraphCommand;

    /// <summary>Remove one or more attachments by id.</summary>
    public sealed record RemoveAttachments(
        IReadOnlyList<AttachmentId> AttachmentIds) : GraphCommand;

    /// <summary>Set a host-defined property on an attachment.</summary>
    public sealed record SetAttachmentProperty(
        AttachmentId Id,
        string Key,
        object? Value) : GraphCommand;

    /// <summary>Reorder the attachments of a single host node.</summary>
    public sealed record ReorderAttachments(
        NodeId HostNodeId,
        IReadOnlyList<AttachmentId> NewOrder) : GraphCommand;

    /// <summary>Move an attachment to a different host node.</summary>
    public sealed record MoveAttachment(
        AttachmentId Id,
        NodeId NewHostNodeId,
        int NewStackIndex) : GraphCommand;

    /// <summary>Multi-step command. The host should treat the contents atomically.</summary>
    public sealed record Batch(string Label, IReadOnlyList<GraphCommand> Commands) : GraphCommand;
}

/// <summary>One element of a multi-node move.</summary>
public readonly record struct NodeMove(NodeId Node, Vector2 NewPosition);

/// <summary>Which end of a link an operation refers to.</summary>
public enum LinkEndpoint
{
    /// <summary>The "from" / output end.</summary>
    Source,

    /// <summary>The "to" / input end.</summary>
    Target,
}

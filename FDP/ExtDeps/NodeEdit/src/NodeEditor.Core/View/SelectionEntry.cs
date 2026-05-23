using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>Identifies one selectable element in the editor.</summary>
public readonly record struct SelectionEntry
{
    public SelectionEntryKind Kind { get; }
    public NodeId Node { get; }
    public LinkId Link { get; }
    public CommentId Comment { get; }
    public RerouteRef Reroute { get; }
    public AttachmentId Attachment { get; }
    public CustomElementRef CustomElement { get; }

    private SelectionEntry(SelectionEntryKind k, NodeId n, LinkId l, CommentId c, RerouteRef r, AttachmentId a, CustomElementRef ce)
    {
        Kind = k; Node = n; Link = l; Comment = c; Reroute = r; Attachment = a; CustomElement = ce;
    }

    public static SelectionEntry OfNode(NodeId id) =>
        new(SelectionEntryKind.Node, id, LinkId.Empty, CommentId.Empty, default, AttachmentId.Empty, default);

    public static SelectionEntry OfLink(LinkId id) =>
        new(SelectionEntryKind.Link, NodeId.Empty, id, CommentId.Empty, default, AttachmentId.Empty, default);

    public static SelectionEntry OfComment(CommentId id) =>
        new(SelectionEntryKind.Comment, NodeId.Empty, LinkId.Empty, id, default, AttachmentId.Empty, default);

    public static SelectionEntry OfReroute(RerouteRef r) =>
        new(SelectionEntryKind.Reroute, NodeId.Empty, LinkId.Empty, CommentId.Empty, r, AttachmentId.Empty, default);

    public static SelectionEntry OfAttachment(AttachmentId id) =>
        new(SelectionEntryKind.Attachment, NodeId.Empty, LinkId.Empty, CommentId.Empty, default, id, default);

    public static SelectionEntry OfCustomElement(CustomElementRef ce) =>
        new(SelectionEntryKind.CustomElement, NodeId.Empty, LinkId.Empty, CommentId.Empty, default, AttachmentId.Empty, ce);
}

public enum SelectionEntryKind { Node, Link, Comment, Reroute, Attachment, CustomElement }

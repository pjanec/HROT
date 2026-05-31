using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Mutable in-memory graph model for the demo.</summary>
public sealed class FakeGraphModel : IGraphModel
{
    private readonly Dictionary<NodeId,        INodeModel>           _nodes       = new();
    private readonly Dictionary<LinkId,        FakeLinkModel>        _links       = new();
    private readonly Dictionary<CommentId,     FakeCommentModel>     _comments    = new();
    private readonly Dictionary<AttachmentId,  FakeAttachmentModel>  _attachments = new();

    public GraphId             Id          { get; }
    public string              DisplayName { get; }
    public GraphKindDescriptor Kind        { get; } =
        new("EventGraph", "Event Graph", AllowsLatent: true, RequiresEntryNode: true);

    public IReadOnlyCollection<INodeModel>    Nodes    => _nodes.Values;
    public IReadOnlyCollection<ILinkModel>    Links    => _links.Values;
    public IReadOnlyCollection<ICommentModel> Comments => _comments.Values;

    public event System.Action<GraphChangeNotification>? Changed;

    public FakeGraphModel(GraphId id, string displayName)
    {
        Id          = id;
        DisplayName = displayName;
    }

    public INodeModel?    FindNode(NodeId id)    => _nodes.TryGetValue(id, out var v)    ? v : null;
    public IPinModel?     FindPin(PinId id)      => FindPinInAllNodes(id);
    public ILinkModel?    FindLink(LinkId id)    => _links.TryGetValue(id, out var v)    ? v : null;
    public ICommentModel? FindComment(CommentId id) => _comments.TryGetValue(id, out var v) ? v : null;

    private IPinModel? FindPinInAllNodes(PinId id)
    {
        foreach (var n in _nodes.Values)
            foreach (var p in n.Pins)
                if (p.Id == id) return p;
        return null;
    }

    // ── IGraphModel attachment members ────────────────────────────────────────

    public IReadOnlyCollection<IAttachmentModel> Attachments =>
        (IReadOnlyCollection<IAttachmentModel>)_attachments.Values;

    public IAttachmentModel? FindAttachment(AttachmentId id) =>
        _attachments.TryGetValue(id, out var v) ? v : null;

    public IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId nodeId) =>
        _attachments.Values.Where(a => a.HostNodeId == nodeId).ToList();

    // ── mutable helpers (called by FakeCommandSink) ───────────────────────────

    public FakeNodeModel AddNode(NodeId id, NodeKindKey kind, string title, Vector2 pos)
    {
        var node = new FakeNodeModel(id, kind, title, pos);
        _nodes[id] = node;
        return node;
    }

    public FakeContainerModel AddContainer(NodeId id, string title, Vector2 pos)
    {
        var c = new FakeContainerModel(id, title, pos);
        _nodes[id] = c;
        return c;
    }

    public void RemoveNode(NodeId id) => _nodes.Remove(id);

    public void SetNodePosition(NodeId id, Vector2 pos)
    {
        if (_nodes.TryGetValue(id, out var n))
        {
            if (n is FakeNodeModel fn) fn.SetPosition(pos);
            else if (n is FakeContainerModel cn) cn.Position = pos;
        }
    }

    public FakeLinkModel AddLink(LinkId id, PinId from, PinId to)
    {
        var link = new FakeLinkModel(id, from, to);
        _links[id] = link;
        return link;
    }

    public void RemoveLink(LinkId id) => _links.Remove(id);

    public FakeCommentModel AddComment(CommentId id, string text, Vector2 pos, Vector2 size, Vector4 color, bool moveWithContents)
    {
        var c = new FakeCommentModel(id, text, pos, size) { Color = color, MoveWithContents = moveWithContents };
        _comments[id] = c;
        return c;
    }

    public void UpdateComment(CommentId id, string? text, Vector2? pos, Vector2? size, Vector4? color, int? zOrder, bool? moveWithContents)
    {
        if (!_comments.TryGetValue(id, out var c)) return;
        if (text     is not null) c.Text     = text;
        if (pos      is not null) c.Position = pos.Value;
        if (size     is not null) c.Size     = size.Value;
        if (color    is not null) c.Color    = color.Value;
        if (zOrder   is not null) c.ZOrder   = zOrder.Value;
        if (moveWithContents is not null) c.MoveWithContents = moveWithContents.Value;
    }

    public void RemoveComment(CommentId id) => _comments.Remove(id);

    public FakeAttachmentModel AddAttachment(
        NodeId hostNodeId,
        AttachmentCategory category,
        string? glyph,
        string? label,
        int stackIndex = 0)
    {
        var id = AttachmentId.NewId();
        var a = new FakeAttachmentModel(id, hostNodeId, category, glyph, label, stackIndex);
        _attachments[id] = a;
        return a;
    }

    public void RemoveAttachment(AttachmentId id) => _attachments.Remove(id);

    public void NotifyChanged(GraphChangeKind kind)
        => Changed?.Invoke(new GraphChangeNotification(kind, null, null, null, null));
}

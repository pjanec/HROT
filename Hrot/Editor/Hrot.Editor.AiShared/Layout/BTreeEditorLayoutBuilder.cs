using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

public sealed class BTreeEditorLayoutBuilder
{
    private Vector2 _panOffset;
    private float _zoomLevel = 1.0f;
    private readonly Dictionary<Guid, NodeLayoutEntry> _nodes = new();

    public BTreeEditorLayoutBuilder Canvas(Vector2 panOffset, float zoomLevel)
    {
        _panOffset = panOffset;
        _zoomLevel = zoomLevel;
        return this;
    }

    public BTreeEditorLayoutBuilder Node(
        string visualId,
        Vector2 position,
        Vector2? sizeOverride = null,
        string? comment = null,
        bool collapsed = false,
        string? color = null,
        string? expressionTarget = null)
    {
        var id = Guid.Parse(visualId);
        _nodes[id] = new NodeLayoutEntry
        {
            Position = position,
            SizeOverride = sizeOverride,
            Comment = comment,
            Collapsed = collapsed,
            Color = color,
            ExpressionTarget = expressionTarget,
        };
        return this;
    }

    public BTreeEditorLayout Build() => new BTreeEditorLayout
    {
        PanOffset = _panOffset,
        ZoomLevel = _zoomLevel,
        Nodes = _nodes,
    };
}

using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

public sealed class BTreeEditorLayout
{
    public Vector2 PanOffset { get; init; }
    public float ZoomLevel { get; init; }
    public IReadOnlyDictionary<Guid, NodeLayoutEntry> Nodes { get; init; } =
        new Dictionary<Guid, NodeLayoutEntry>();
}

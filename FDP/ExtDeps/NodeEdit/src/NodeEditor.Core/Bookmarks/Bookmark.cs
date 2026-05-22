using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Core.Bookmarks;

/// <summary>One bookmark targeting a specific viewport on a graph.</summary>
public sealed record Bookmark(
    string BookmarkId,
    GraphId TargetGraph,
    string Label,
    Vector2 ViewportPan,
    float ViewportZoom,
    int SlotNumber,
    DateTime CreatedAt);

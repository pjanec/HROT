using System.Collections.Generic;
using System.Numerics;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Snapshot of canvas-level layout data for a BTree asset.
/// Returned by a <see cref="BTreeLayoutAttribute"/>-decorated method; consumed by the editor on open.
/// </summary>
public sealed class BTreeEditorLayout
{
    public Vector2 PanOffset { get; init; }
    public float ZoomLevel { get; init; }
    public IReadOnlyDictionary<Guid, NodeLayoutEntry> Nodes { get; init; } =
        new Dictionary<Guid, NodeLayoutEntry>();

    /// <summary>Sync bindings per subtree-node visual ID. Empty when none configured.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<SubtreeSyncBinding>> SyncBindings { get; init; } =
        new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>();

    public IReadOnlyList<(string VariableName, string WriterPairKey)> BlackboardConflictSuppressions { get; init; } =
        Array.Empty<(string, string)>();
    public IReadOnlyList<string> UnusedWarningSuppressions { get; init; } =
        Array.Empty<string>();

    /// <summary>⭐ <c>W7b</c> (§9.4) — variables whose cross-region concurrent writes the designer
    /// explicitly allowed. ⛔ PER VARIABLE, unlike the per-(variable, writer-pair) conflict
    /// suppressions above.</summary>
    public IReadOnlyList<string> ConcurrentWritesAllowed { get; init; } =
        Array.Empty<string>();

    /// <summary>
    /// Per-child-node waypoints for the edge from that child up to its parent.
    /// Key = child node's VisualId. Empty when no reroute points exist.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<Vector2>> LinkWaypoints { get; init; } =
        new Dictionary<Guid, IReadOnlyList<Vector2>>();
}

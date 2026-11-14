using System.Collections.Generic;
using System.Numerics;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Fluent builder for <see cref="BTreeEditorLayout"/> instances.
/// Used inside <see cref="BTreeLayoutAttribute"/>-decorated methods in game-side assemblies.
/// </summary>
public sealed class BTreeEditorLayoutBuilder
{
    private Vector2 _panOffset;
    private float _zoomLevel = 1.0f;
    private readonly Dictionary<Guid, NodeLayoutEntry> _nodes = new();
    private readonly Dictionary<Guid, List<SubtreeSyncBinding>> _syncBindings = new();
    private readonly List<(string VariableName, string WriterPairKey)> _conflictSuppressions = new();
    private readonly List<string> _unusedSuppressions = new();
    private readonly List<string> _concurrentWritesAllowed = new();
    private readonly Dictionary<Guid, IReadOnlyList<Vector2>> _linkWaypoints = new();

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

    public BTreeEditorLayoutBuilder SubtreeSyncField(
        string visualId,
        string fieldName,
        string? masterVar,
        bool syncIn,
        bool syncOut)
    {
        var id = Guid.Parse(visualId);
        if (!_syncBindings.TryGetValue(id, out var list))
        {
            list = new List<SubtreeSyncBinding>();
            _syncBindings[id] = list;
        }
        list.Add(new SubtreeSyncBinding(fieldName, masterVar, syncIn, syncOut));
        return this;
    }

    public BTreeEditorLayoutBuilder SuppressBlackboardConflict(string variableName, string writerPairKey)
    {
        _conflictSuppressions.Add((variableName, writerPairKey));
        return this;
    }

    public BTreeEditorLayoutBuilder SuppressUnusedWarning(string variableName)
    {
        _unusedSuppressions.Add(variableName);
        return this;
    }

    /// <summary>⭐ <c>W7b</c> (§9.4) — the designer allowed concurrent cross-region writes to
    /// this variable. ⛔ PER VARIABLE; see <c>SuppressBlackboardConflict</c> for the per-pair one.</summary>
    public BTreeEditorLayoutBuilder AllowConcurrentWrites(string variableName)
    {
        _concurrentWritesAllowed.Add(variableName);
        return this;
    }

    /// <summary>
    /// Records waypoints for the edge from the child node (identified by <paramref name="childVisualId"/>)
    /// up to its parent. Called in the emitted <c>[BTreeLayout]</c> method only when waypoints exist.
    /// </summary>
    public BTreeEditorLayoutBuilder LinkWaypoints(string childVisualId, Vector2[] waypoints)
    {
        _linkWaypoints[Guid.Parse(childVisualId)] = waypoints;
        return this;
    }

    public BTreeEditorLayout Build() => new BTreeEditorLayout
    {
        PanOffset = _panOffset,
        ZoomLevel = _zoomLevel,
        Nodes = _nodes,
        SyncBindings = _syncBindings.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<SubtreeSyncBinding>)kv.Value.AsReadOnly()),
        BlackboardConflictSuppressions = _conflictSuppressions,
        UnusedWarningSuppressions = _unusedSuppressions,
        ConcurrentWritesAllowed   = _concurrentWritesAllowed,
        LinkWaypoints = _linkWaypoints,
    };
}

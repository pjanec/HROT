using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Fluent builder for <see cref="HsmEditorLayout"/> instances.
/// Used inside <see cref="HsmLayoutAttribute"/>-decorated methods in game-side assemblies.
/// </summary>
public sealed class HsmEditorLayoutBuilder
{
    private Vector2 _panOffset;
    private float _zoomLevel = 1.0f;
    private readonly Dictionary<Guid, StateLayoutEntry> _states = new();
    private readonly Dictionary<Guid, TransitionLayoutEntry> _transitions = new();
    private readonly Dictionary<Guid, RegionLayoutEntry> _regions = new();
    private readonly List<(string VariableName, string WriterPairKey)> _conflictSuppressions = new();
    private readonly List<string> _unusedSuppressions = new();
    private readonly List<string> _concurrentWritesAllowed = new();

    public HsmEditorLayoutBuilder Canvas(Vector2 panOffset, float zoomLevel)
    {
        _panOffset = panOffset;
        _zoomLevel = zoomLevel;
        return this;
    }

    public HsmEditorLayoutBuilder State(
        string stableId,
        Vector2 position,
        Vector2? sizeOverride = null,
        string? comment = null,
        bool collapsed = false,
        string? color = null)
    {
        var id = Guid.Parse(stableId);
        _states[id] = new StateLayoutEntry
        {
            Position = position,
            SizeOverride = sizeOverride,
            Comment = comment,
            Collapsed = collapsed,
            Color = color,
        };
        return this;
    }

    public HsmEditorLayoutBuilder Transition(
        string visualId,
        Vector2[] waypoints,
        string? comment = null,
        string? color = null)
    {
        var id = Guid.Parse(visualId);
        _transitions[id] = new TransitionLayoutEntry
        {
            Waypoints = waypoints,
            Comment = comment,
            Color = color,
        };
        return this;
    }

    public HsmEditorLayoutBuilder Region(
        string stableId,
        int regionIndex,
        Vector2 position,
        Vector2? sizeOverride = null,
        string? comment = null,
        bool collapsed = false,
        string? color = null)
    {
        // Region key is stableId; regionIndex is stored for stable lookup after deletion.
        var id = Guid.Parse(stableId);
        _regions[id] = new RegionLayoutEntry
        {
            Position = position,
            SizeOverride = sizeOverride,
            Comment = comment,
            Collapsed = collapsed,
            Color = color,
            RegionIndex = regionIndex,
        };
        return this;
    }

    public HsmEditorLayoutBuilder SuppressBlackboardConflict(string variableName, string writerPairKey)
    {
        _conflictSuppressions.Add((variableName, writerPairKey));
        return this;
    }

    public HsmEditorLayoutBuilder SuppressUnusedWarning(string variableName)
    {
        _unusedSuppressions.Add(variableName);
        return this;
    }

    /// <summary>⭐ <c>W7b</c> (§9.4) — the designer allowed concurrent cross-region writes to
    /// this variable. ⛔ PER VARIABLE; see <c>SuppressBlackboardConflict</c> for the per-pair one.</summary>
    public HsmEditorLayoutBuilder AllowConcurrentWrites(string variableName)
    {
        _concurrentWritesAllowed.Add(variableName);
        return this;
    }

    public HsmEditorLayout Build() => new HsmEditorLayout
    {
        PanOffset = _panOffset,
        ZoomLevel = _zoomLevel,
        States = _states,
        Transitions = _transitions,
        Regions = _regions,
        BlackboardConflictSuppressions = _conflictSuppressions,
        UnusedWarningSuppressions = _unusedSuppressions,
        ConcurrentWritesAllowed   = _concurrentWritesAllowed,
    };
}

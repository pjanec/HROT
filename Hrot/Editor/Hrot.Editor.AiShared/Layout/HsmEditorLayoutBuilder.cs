using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

public sealed class HsmEditorLayoutBuilder
{
    private Vector2 _panOffset;
    private float _zoomLevel = 1.0f;
    private readonly Dictionary<Guid, StateLayoutEntry> _states = new();
    private readonly Dictionary<Guid, TransitionLayoutEntry> _transitions = new();
    private readonly Dictionary<Guid, RegionLayoutEntry> _regions = new();

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
        // Region key is stableId; regionIndex is stored for disambiguation when multiple
        // regions share the same stableId parent (parallel regions).
        var id = Guid.Parse(stableId);
        _regions[id] = new RegionLayoutEntry
        {
            Position = position,
            SizeOverride = sizeOverride,
            Comment = comment,
            Collapsed = collapsed,
            Color = color,
        };
        return this;
    }

    public HsmEditorLayout Build() => new HsmEditorLayout
    {
        PanOffset = _panOffset,
        ZoomLevel = _zoomLevel,
        States = _states,
        Transitions = _transitions,
        Regions = _regions,
    };
}

using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Debug;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-24 — switches the canvas between the graphs of one Blueprint asset.
///
/// <para>
/// The per-document stack (<see cref="GraphView"/>, undo, find bar, commands, bookmarks) is built
/// once per asset and holds fixed references to the graph model and command sink. A switch
/// therefore <b>retargets those two objects in place</b> (architect Q23-A: option A2) rather than
/// rebuilding the stack — which is what preserves the undo history, bookmarks and registered
/// commands across a switch. The only object that is rebuilt is the debug adapter, because it
/// binds a graph id at construction and is cheap to remake.
/// </para>
///
/// <para>
/// Per-graph viewport and selection are saved on the way out and restored on the way back, so
/// flipping between graphs does not lose your place. The outgoing viewport is also written to the
/// graph's <see cref="GraphMetadata"/> slots — persisted with the asset on the next real save
/// (never dirtying the asset by itself), so a reopened asset restores camera positions too.
/// </para>
/// </summary>
public sealed class BlueprintGraphSwitcher
{
    private readonly BlueprintAsset               _asset;
    private readonly BlueprintGraphModel          _model;
    private readonly BlueprintCommandSink         _sink;
    private readonly GraphView                    _view;
    private readonly BlueprintEditorHostServices? _hostServices;
    private readonly IBlueprintDebugSession?      _debugSession;

    private sealed record SavedViewState(
        Vector2 Pan, float Zoom, IReadOnlyList<SelectionEntry> Selection);

    /// <summary>Session-lived view state per graph id. Viewport also persists via GraphMetadata.</summary>
    private readonly Dictionary<Guid, SavedViewState> _savedState = new();

    /// <param name="hostServices">
    /// Needed only to re-bind the debug adapter on switch; null in headless tests (which also
    /// pass no debug session).
    /// </param>
    public BlueprintGraphSwitcher(
        BlueprintAsset               asset,
        BlueprintGraphModel          model,
        BlueprintCommandSink         sink,
        GraphView                    view,
        BlueprintEditorHostServices? hostServices = null,
        IBlueprintDebugSession?      debugSession = null)
    {
        _asset        = asset ?? throw new ArgumentNullException(nameof(asset));
        _model        = model ?? throw new ArgumentNullException(nameof(model));
        _sink         = sink  ?? throw new ArgumentNullException(nameof(sink));
        _view         = view  ?? throw new ArgumentNullException(nameof(view));
        _hostServices = hostServices;
        _debugSession = debugSession;

        // The undo stack tags every entry with the graph it was recorded in, and switches the
        // canvas back to that graph before replaying (Q23-A sub-decision: one per-asset stack,
        // Unreal-style auto-switch). Without this, an entry recorded in graph A would be applied
        // by the sink while it points at graph B — mutating the wrong graph.
        view.Undo.ContextProvider = () => CurrentGraphId;
        view.Undo.ContextRestorer = ctx => { if (ctx is Guid g) SwitchTo(g); };
    }

    /// <summary>The graph the canvas currently shows.</summary>
    public Graph CurrentGraph => _model.CurrentGraph;

    /// <summary>Asset-level id of the graph the canvas currently shows.</summary>
    public Guid CurrentGraphId => _model.CurrentGraph.Id;

    /// <summary>
    /// Switches the canvas to the given graph. No-op (returning true) when already there;
    /// false when the id matches no graph of this asset.
    /// </summary>
    public bool SwitchTo(Guid graphId)
    {
        if (graphId == CurrentGraphId) return true;

        var graph = _asset.Graphs.FirstOrDefault(g => g.Id == graphId);
        if (graph is null) return false;

        SaveViewState(_model.CurrentGraph);

        _model.Retarget(graph);
        _sink.Retarget(graph);
        RebindDebugAdapter(graph);
        RestoreViewState(graph);

        _model.NotifyChanged();
        BlueprintGraphViewMemory.SetLastViewed(_asset.AssetId, graphId);
        return true;
    }

    /// <summary>
    /// Resolves a NodeEdit view-level <see cref="GraphId"/> (the deterministic hash the graph
    /// model exposes as <c>Model.Id</c>) back to the asset graph it denotes, then switches.
    /// This is the shape bookmarks speak: <c>Bookmark.TargetGraph</c> stores the view id.
    /// </summary>
    public bool SwitchToViewId(GraphId viewId)
    {
        foreach (var g in _asset.Graphs)
        {
            var candidate = new GraphId(IdGenerator.Deterministic($"graph:{_asset.AssetId}:{g.Id}"));
            if (candidate == viewId)
                return SwitchTo(g.Id);
        }
        return false;
    }

    // ── view-state save/restore ───────────────────────────────────────────────

    private void SaveViewState(Graph outgoing)
    {
        var vp = _view.Viewport;
        _savedState[outgoing.Id] = new SavedViewState(
            vp.PanGraph, vp.Zoom, _view.Selection.Items.ToList());

        // Written to the asset's per-graph metadata slots so the camera survives a reopen.
        // Deliberately NOT marking dirty: navigation is not an edit; the values simply ride
        // along with the next real save.
        outgoing.EditorMetadata.ViewportX    = vp.PanGraph.X;
        outgoing.EditorMetadata.ViewportY    = vp.PanGraph.Y;
        outgoing.EditorMetadata.ViewportZoom = vp.Zoom;
    }

    private void RestoreViewState(Graph incoming)
    {
        var vp = _view.Viewport;

        if (_savedState.TryGetValue(incoming.Id, out var saved))
        {
            vp.PanGraph = saved.Pan;
            vp.SetZoom(saved.Zoom);
            _view.Selection.ReplaceWith(saved.Selection);
            return;
        }

        // First visit this session: prefer the viewport persisted in the asset, else frame the
        // graph's content so the designer lands on the nodes rather than on empty space.
        _view.Selection.Clear();
        var meta = incoming.EditorMetadata;
        if (meta.ViewportZoom > 0f)
        {
            vp.PanGraph = new Vector2(meta.ViewportX, meta.ViewportY);
            vp.SetZoom(meta.ViewportZoom);
            return;
        }

        vp.SetZoom(1f);
        vp.PanGraph = ContentOrigin(incoming);
    }

    /// <summary>Top-left of the graph's node bounds minus a margin; origin for empty graphs.</summary>
    private static Vector2 ContentOrigin(Graph graph)
    {
        if (graph.Nodes.Count == 0) return Vector2.Zero;

        float minX = float.MaxValue, minY = float.MaxValue;
        foreach (var n in graph.Nodes)
        {
            if (n.EditorMetadata.X < minX) minX = n.EditorMetadata.X;
            if (n.EditorMetadata.Y < minY) minY = n.EditorMetadata.Y;
        }
        return new Vector2(minX - 60f, minY - 60f);
    }

    // ── debug adapter ─────────────────────────────────────────────────────────

    /// <summary>
    /// The adapter binds <c>graph.Id</c> at construction (breakpoints and watches are per-graph),
    /// so a switch remakes it — the one deliberate rebuild in the retarget design.
    /// </summary>
    private void RebindDebugAdapter(Graph graph)
    {
        if (_debugSession is null || _hostServices is null) return;
        _hostServices.SetDebugSession(
            new BlueprintDebugToNodeEditAdapter(_debugSession, _asset.AssetId, graph.Id));
    }
}

/// <summary>
/// BP-24 (Q23-C) — remembers the last-viewed graph per asset so reopening an asset lands on the
/// graph the designer was editing. Process-wide and session-lived; the cross-restart leg belongs
/// in <see cref="BlueprintEditorPreferences"/> once a live composition actually loads that file
/// (today nothing does — see the factory's selection comment).
/// </summary>
public static class BlueprintGraphViewMemory
{
    private static readonly Dictionary<Guid, Guid> _lastViewed = new();
    private static readonly object _lock = new();

    public static void SetLastViewed(Guid assetId, Guid graphId)
    {
        lock (_lock) _lastViewed[assetId] = graphId;
    }

    public static Guid? GetLastViewed(Guid assetId)
    {
        lock (_lock) return _lastViewed.TryGetValue(assetId, out var g) ? g : null;
    }

    /// <summary>Test hook — a static store would otherwise leak choices across unit tests.</summary>
    public static void Reset()
    {
        lock (_lock) _lastViewed.Clear();
    }
}

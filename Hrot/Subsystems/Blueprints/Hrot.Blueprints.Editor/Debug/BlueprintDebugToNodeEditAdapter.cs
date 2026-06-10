using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// Bridges <see cref="Hrot.Blueprints.Core.Debug.IBlueprintDebugSession"/> to NodeEdit's
/// <see cref="IDebugSession"/> so that <see cref="NodeEditor.UI.Canvas.NodeRenderer"/>
/// natively draws breakpoint markers and execution overlays without any Blueprint-specific
/// awareness.
///
/// <para>NGS-2.4b: <see cref="CurrentlyExecutingNode"/> follows the virtual pointer while
/// paused and recordings exist. The session raises <see cref="IBlueprintDebugSession.OnSessionStateChanged"/>
/// on every pointer move (StepBack/StepInto/StepOver/StepOut), which this adapter forwards to
/// <see cref="StateChanged"/> so the canvas redraws automatically.</para>
/// </summary>
public sealed class BlueprintDebugToNodeEditAdapter : IDebugSession
{
    private readonly Hrot.Blueprints.Core.Debug.IBlueprintDebugSession _session;
    private readonly Guid _assetId;
    private readonly Guid _graphId;

    public BlueprintDebugToNodeEditAdapter(
        Hrot.Blueprints.Core.Debug.IBlueprintDebugSession session,
        Guid assetId,
        Guid graphId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _assetId = assetId;
        _graphId = graphId;
    }

    // ── IDebugSession ──────────────────────────────────────────────────────

    public bool IsAttached => true;

    public bool IsPaused => _session.IsPaused;

    /// <summary>
    /// Returns the node that should be highlighted on the canvas.
    ///
    /// Priority while paused:
    /// <list type="number">
    ///   <item>Virtual pointer's current node (<see cref="IBlueprintDebugSession.CurrentNodeId"/>)
    ///         when the session is paused and the pointer is active (pointer ≥ 0). This makes
    ///         StepBack/StepInto move the canvas highlight in real time.</item>
    ///   <item>The paused-at breakpoint node (<see cref="IBlueprintDebugSession.PausedAt"/>) as
    ///         before — used when no recordings exist for the paused entity (CF-6 path).</item>
    ///   <item>Most-recent node from execution history (existing live-running overlay).</item>
    /// </list>
    /// </summary>
    public NodeId? CurrentlyExecutingNode
    {
        get
        {
            // NGS-2.4b: while paused and the virtual pointer is active, follow the pointer.
            if (_session.IsPaused && _session.CurrentNodePointer >= 0)
            {
                var ptrNodeId = _session.CurrentNodeId;
                if (ptrNodeId is not null &&
                    Guid.TryParse(ptrNodeId, out var ptrGuid) &&
                    ptrGuid != Guid.Empty)
                    return new NodeId(ptrGuid);
            }

            // Check the paused-at node (most precise non-pointer fallback).
            var paused = _session.PausedAt;
            if (paused != null && Guid.TryParse(paused.NodeId, out var pausedGuid) && pausedGuid != Guid.Empty)
                return new NodeId(pausedGuid);

            // Fall back to recent execution history.
            var history = _session.GetRecentNodeHistory(1);
            if (history.Count > 0 && Guid.TryParse(history[0].NodeIdString, out var histGuid) && histGuid != Guid.Empty)
                return new NodeId(histGuid);

            return null;
        }
    }

    public IReadOnlySet<NodeId> RecentlyExecutedNodes
    {
        get
        {
            var history = _session.GetRecentNodeHistory(10);
            var set = new HashSet<NodeId>();
            foreach (var entry in history)
            {
                if (Guid.TryParse(entry.NodeIdString, out var guid) && guid != Guid.Empty)
                    set.Add(new NodeId(guid));
            }
            return set;
        }
    }

    public IReadOnlySet<NodeId> Breakpoints
    {
        get
        {
            var bps = _session.GetBreakpoints();
            var set = new HashSet<NodeId>();
            foreach (var bp in bps)
            {
                if (bp.AssetId == _assetId && bp.GraphId == _graphId
                    && Guid.TryParse(bp.NodeId, out var guid) && guid != Guid.Empty)
                {
                    set.Add(new NodeId(guid));
                }
            }
            return set;
        }
    }

    public IReadOnlySet<PinId> WatchedPins
    {
        get
        {
            var watches = _session.GetWatches();
            var set = new HashSet<PinId>();
            foreach (var w in watches)
            {
                if (w.AssetId == _assetId && w.GraphId == _graphId)
                    set.Add(new PinId(w.PinId));
            }
            return set;
        }
    }

    public void ToggleBreakpoint(NodeId node)
    {
        var nodeIdStr = node.Value.ToString("D");
        var bps = _session.GetBreakpoints();
        var existing = bps.FirstOrDefault(bp =>
            bp.AssetId == _assetId && bp.GraphId == _graphId && bp.NodeId == nodeIdStr);
        if (existing != null)
            _session.ClearBreakpoint(existing.Id);
        else
            _session.SetBreakpoint(_assetId, _graphId, node.Value);
    }

    public void ToggleWatch(PinId pin)
    {
        var watches = _session.GetWatches();
        var existing = watches.FirstOrDefault(w =>
            w.AssetId == _assetId && w.GraphId == _graphId && w.PinId == pin.Value);
        if (existing != null)
            _session.RemoveWatch(existing.Id);
        else
            _session.AddWatch(_assetId, _graphId, pin.Value, pin.Value.ToString("N"), typeof(object));
    }

    public void Continue() => _session.Continue();
    public void StepOver() => _session.StepOver();
    public void StepInto() => _session.StepInto();
    public void StepOut()  => _session.StepOut();

    public object? GetWatchValue(PinId pin)
    {
        // Watch values are rendered by WatchPanelWindow via the native session,
        // not through this path. Return null for now.
        return null;
    }

    public event System.Action? StateChanged;

    public void Subscribe()
    {
        _session.OnNodeExecuted += OnSessionActivity;
        _session.OnPinValueChangedEvent += OnSessionActivity;
        _session.OnSessionStateChanged += OnSessionActivity;
    }

    public void Unsubscribe()
    {
        _session.OnNodeExecuted -= OnSessionActivity;
        _session.OnPinValueChangedEvent -= OnSessionActivity;
        _session.OnSessionStateChanged -= OnSessionActivity;
    }

    private void OnSessionActivity(Hrot.Blueprints.Core.Debug.NodeExecuted _) => StateChanged?.Invoke();
    private void OnSessionActivity(Hrot.Blueprints.Core.Debug.PinValueChanged _) => StateChanged?.Invoke();
    private void OnSessionActivity() => StateChanged?.Invoke();
}

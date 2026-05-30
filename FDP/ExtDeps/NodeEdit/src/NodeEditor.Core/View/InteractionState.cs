using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// All transient editor state that is not in the host data model and not in the viewport.
/// Includes the current interaction mode, what the cursor is over, drag bookkeeping
/// (per-node graph-space overrides during a drag), and the pending-wire descriptor.
/// </summary>
public sealed class InteractionState
{
    public InteractionMode Mode { get; set; } = InteractionMode.Idle;

    public HoverInfo Hover { get; set; } = HoverInfo.None;

    /// <summary>Screen position where the current LMB drag (if any) began.</summary>
    public Vector2 DragStartScreen { get; set; }

    /// <summary>Graph-space position where the current LMB drag began.</summary>
    public Vector2 DragStartGraph { get; set; }

    /// <summary>True once the cursor has moved past the drag threshold since LMB-down.</summary>
    public bool DragThresholdCrossed { get; set; }

    /// <summary>Marquee rect in graph space (only valid while Mode == MarqueeSelecting).</summary>
    public RectF MarqueeGraph { get; set; }

    /// <summary>Whether the marquee uses touch (Alt) instead of fully-enclosed mode.</summary>
    public bool MarqueeTouchMode { get; set; }

    /// <summary>
    /// Per-node graph-space position overrides while a drag is in progress.
    /// The renderer reads from here in preference to the host model; on mouse-up
    /// the final positions are flushed via a single MoveNodes command and this dict is cleared.
    /// </summary>
    public Dictionary<NodeId, Vector2> DragOverridePositions { get; } = new();
    /// <summary>Per-pin override values during an inline editor drag.</summary>
    public Dictionary<PinId, object?> PinDragOverrides { get; } = new();

    /// <summary>Snapshot of nodes that are dragged together with a comment ("contained" set, captured at drag-start).</summary>
    public HashSet<NodeId> CommentDragContents { get; } = new();

    /// <summary>Per-comment graph-space position overrides while a comment drag is in progress.</summary>
    public Dictionary<CommentId, Vector2> CommentDragOverridePositions { get; } = new();

    /// <summary>The comment currently being inline-renamed; null when not renaming.</summary>
    public CommentId? RenamingComment { get; set; }

    /// <summary>The pending-wire descriptor, set while Mode == PendingWire.</summary>
    public PendingWire? PendingWire { get; set; }

    /// <summary>Screen position of the right-click that opened a context menu (if any).</summary>
    public Vector2? ContextMenuScreen { get; set; }
    /// <summary>Snapshot of the hovered target captured when context menu was requested.</summary>
    public HoverInfo ContextMenuTarget { get; set; } = HoverInfo.None;

    /// <summary>
    /// During a node drag: the container the dragged node(s) would be dropped into, or null for root.
    /// Cleared on drag end.
    /// </summary>
    public NodeId? DropTargetContainerId { get; set; }

    /// <summary>True when the current drop target would create a cycle and the drop is therefore invalid.</summary>
    public bool DropTargetCycleDetected { get; set; }

    /// <summary>Optional active viewport tween (camera animation to a bookmark).</summary>
    public ViewportTween? ActiveTween { get; private set; }

    /// <summary>Begin a smooth camera animation to the given pan/zoom over the specified duration.</summary>
    public void BeginViewportTween(Vector2 targetPan, float targetZoom, double durationMs)
        => ActiveTween = new ViewportTween(targetPan, targetZoom, durationMs);

    /// <summary>Clear the active tween (called by renderer once the tween completes or is interrupted).</summary>
    public void ClearTween() => ActiveTween = null;

    /// <summary>Reset to Idle: clears mode, drag overrides, marquee, pending wire, drop target.</summary>
    public void ResetToIdle()
    {
        Mode = InteractionMode.Idle;
        DragThresholdCrossed = false;
        DragOverridePositions.Clear();
        PinDragOverrides.Clear();
        CommentDragContents.Clear();
        CommentDragOverridePositions.Clear();
        RenamingComment = null;
        MarqueeGraph = default;
        MarqueeTouchMode = false;
        PendingWire = null;
        ContextMenuScreen = null;
        ContextMenuTarget = HoverInfo.None;
        DropTargetContainerId = null;
        DropTargetCycleDetected = false;
    }
}

/// <summary>Describes a camera-to-bookmark viewport animation.</summary>
public sealed class ViewportTween
{
    public Vector2 TargetPan  { get; }
    public float   TargetZoom { get; }
    public double  DurationMs { get; }
    public double  ElapsedMs  { get; private set; }

    public ViewportTween(Vector2 targetPan, float targetZoom, double durationMs)
    {
        TargetPan  = targetPan;
        TargetZoom = targetZoom;
        DurationMs = durationMs;
    }

    public bool IsComplete => ElapsedMs >= DurationMs;

    /// <summary>Advance the tween by <paramref name="deltaMs"/> and return the progress 0..1 (ease-out).</summary>
    public float Advance(double deltaMs)
    {
        ElapsedMs += deltaMs;
        var t = (float)Math.Min(ElapsedMs / DurationMs, 1.0);
        return 1f - (1f - t) * (1f - t); // ease-out quadratic
    }
}

namespace NodeEditor.Core.View;

/// <summary>
/// Top-level state of the canvas interaction state machine.
/// Exactly one mode is active at any time.
/// </summary>
public enum InteractionMode
{
    /// <summary>Mouse is hovering or idle; clicks may trigger transitions.</summary>
    Idle,
    /// <summary>RMB-drag panning.</summary>
    Panning,
    /// <summary>Selected nodes are being dragged (drag threshold has been crossed).</summary>
    DraggingNodes,
    /// <summary>One or more reroute waypoints are being dragged.</summary>
    DraggingReroutes,
    /// <summary>A comment box is being moved.</summary>
    DraggingComment,
    /// <summary>A comment box is being resized.</summary>
    ResizingComment,
    /// <summary>LMB-drag from empty canvas is drawing a marquee selection rect.</summary>
    MarqueeSelecting,
    /// <summary>LMB-drag from a pin is drawing a pending connection wire.</summary>
    PendingWire,
    /// <summary>The contextual node-creation picker is open and consuming input.</summary>
    PickerOpen
}

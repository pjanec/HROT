using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// State for a wire currently being dragged from a source pin.
/// While set, <see cref="InteractionMode.PendingWire"/> is active.
/// </summary>
public sealed class PendingWire
{
    /// <summary>Pin the drag started from.</summary>
    public required PinId SourcePin { get; init; }

    /// <summary>Current mouse position in graph space (updated every frame).</summary>
    public Vector2 CursorGraph { get; set; }

    /// <summary>
    /// Optional candidate target pin under the cursor (within snap radius).
    /// Snap radius defined in <c>TimingConstants.PinSnapRadiusPx</c>.
    /// </summary>
    public PinId? CandidateTarget { get; set; }

    /// <summary>Whether the candidate is a valid connection per the validator.</summary>
    public bool CandidateValid { get; set; }

    /// <summary>Whether the candidate would require an auto-cast (validator returned ValidWithCast).</summary>
    public bool CandidateNeedsCast { get; set; }

    /// <summary>
    /// CE-1000 (node-to-node routing): the node the link starts from. Set when the drag began on a node's border
    /// band or as a Shift-drag on the node, rather than on a pin.
    /// </summary>
    public NodeId? SourceNode { get; init; }

    /// <summary>CE-1000: the node under the cursor that the link would connect to (drawn as a border-to-border preview).</summary>
    public NodeId? CandidateNode { get; set; }

    /// <summary>
    /// CE-1000: true once the cursor has left <see cref="SourceNode"/>. A release back on the source before that is a
    /// click, never a self-transition.
    /// </summary>
    public bool LeftSourceNode { get; set; }

    /// <summary>CE-1000: a release without dragging ADDS the source node to the selection (Shift) instead of replacing it.</summary>
    public bool AddToSelectionOnClick { get; init; }

    /// <summary>
    /// CE-1001: the wire was started from a menu ("Add Transition") and follows the cursor with no button held;
    /// a left-click finishes it, Escape or a right-click cancels.
    /// </summary>
    public bool Sticky { get; init; }
}

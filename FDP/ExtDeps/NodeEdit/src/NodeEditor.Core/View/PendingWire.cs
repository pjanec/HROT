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
}

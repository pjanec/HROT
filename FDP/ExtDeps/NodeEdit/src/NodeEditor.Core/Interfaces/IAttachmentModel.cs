using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Read-only view of one attachment pinned to a host node.
/// Implemented by the host; the editor never mutates this directly.
/// </summary>
public interface IAttachmentModel
{
    AttachmentId Id { get; }
    NodeId HostNodeId { get; }

    /// <summary>
    /// Stable categorization. Determines header color and default visual.
    /// Host-defined; NodeEditor does not interpret the value.
    /// </summary>
    AttachmentCategory Category { get; }

    /// <summary>
    /// Optional short glyph rendered first in the pill body.
    /// One or two characters; rendered larger than the label.
    /// Null means no glyph.
    /// </summary>
    string? Glyph { get; }

    /// <summary>
    /// Optional one-line label rendered after the glyph.
    /// Truncated with ellipsis if too long.
    /// Null means no label (glyph-only pill).
    /// </summary>
    string? Label { get; }

    /// <summary>Tooltip on hover. Multi-line allowed.</summary>
    string? Tooltip { get; }

    /// <summary>
    /// State flags affecting visual treatment.
    /// Identical semantics to NodeState for the shared bits.
    /// </summary>
    AttachmentState State { get; }

    /// <summary>
    /// Ordering position within the host's attachment stack.
    /// Lower values render to the left; equal values are stable-sorted by Id.
    /// </summary>
    int StackIndex { get; }
}

/// <summary>Stable categorization for an attachment.</summary>
public enum AttachmentCategory
{
    /// <summary>BTree decorator (Inverter, Repeater, etc.).</summary>
    Decorator,
    /// <summary>HSM state flag (deferred-events, has-history, conflict).</summary>
    Flag,
    /// <summary>Blueprint pure-call (future use).</summary>
    Pure,
    /// <summary>Host-defined; uses theme custom color.</summary>
    Custom,
}

/// <summary>State flags for visual treatment of an attachment.</summary>
[Flags]
public enum AttachmentState
{
    Normal           = 0,
    Disabled         = 1 << 0,
    Error            = 1 << 1,
    Warning          = 1 << 2,
    Executing        = 1 << 3,   // debug only
    RecentlyExecuted = 1 << 4,   // debug only
    Selected         = 1 << 5,   // editor-managed, never set by host
}

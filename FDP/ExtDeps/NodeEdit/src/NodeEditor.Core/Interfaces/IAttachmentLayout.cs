using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// The computed position of a single attachment pill within the host's stack.
/// All coordinates are relative to the host node's top-left corner.
/// Positive Y is downward (canvas coordinate convention).
/// Attachments appear above the host, so Y values will be negative.
/// </summary>
public readonly record struct AttachmentPlacement(
    AttachmentId Id,
    Vector2 TopLeft,
    Vector2 Size);

/// <summary>Result of laying out all attachments for one host node.</summary>
public sealed class AttachmentLayout
{
    /// <summary>Placements indexed by attachment id.</summary>
    public IReadOnlyDictionary<AttachmentId, AttachmentPlacement> Placements { get; }

    /// <summary>
    /// Total height of the attachment stack above the host, including the gap.
    /// Zero when there are no attachments.
    /// </summary>
    public float TotalHeightAboveHost { get; }

    public AttachmentLayout(
        IReadOnlyDictionary<AttachmentId, AttachmentPlacement> placements,
        float totalHeightAboveHost)
    {
        Placements = placements;
        TotalHeightAboveHost = totalHeightAboveHost;
    }

    /// <summary>An empty layout (no attachments).</summary>
    public static AttachmentLayout Empty { get; } =
        new(new Dictionary<AttachmentId, AttachmentPlacement>(), 0f);
}

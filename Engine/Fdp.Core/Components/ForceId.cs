namespace Fdp.Core;

/// <summary>
/// Force affiliation of an entity used for visual rendering and tactical identification.
///
/// Maps to <see cref="Hrot.NED.Descriptors.eForceIdentifier"/> from the DDS data model.
/// Stored as a <see cref="byte"/> so it fits inside the flat <see cref="ResolvedStyle"/>
/// layout without padding (§CODE-STANDARDS §5).
/// </summary>
public enum ForceId : byte
{
    /// <summary>Neutral entity. Zero value / default. Rendered as green.</summary>
    Neutral = 0,

    /// <summary>Friendly / blue-force entity. Rendered as blue.</summary>
    Friend = 1,

    /// <summary>Hostile / opposing-force entity. Rendered as red.</summary>
    Hostile = 2,
}

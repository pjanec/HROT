namespace Bagira.IG.Components;

/// <summary>
/// Force affiliation of an entity used for visual rendering and tactical identification.
///
/// Maps to <see cref="Bagira.BDC.SSTD.eForceIdentifier"/> from the DDS data model.
/// Stored as a <see cref="byte"/> so it fits inside the flat <see cref="ResolvedStyle"/>
/// layout without padding (§CODE-STANDARDS §5).
/// </summary>
public enum ForceId : byte
{
    /// <summary>Affiliation not established. Rendered as white (default/safe state).</summary>
    Unknown = 0,

    /// <summary>Friendly / blue-force entity. Rendered as blue.</summary>
    Friend = 1,

    /// <summary>Hostile / opposing-force entity. Rendered as red.</summary>
    Hostile = 2,

    /// <summary>Neutral entity. Rendered as green.</summary>
    Neutral = 3,
}

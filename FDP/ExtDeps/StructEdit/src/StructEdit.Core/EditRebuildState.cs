namespace StructEdit.Core;

/// <summary>
/// Indicates whether the EditDocument needs to be rebuilt after a change.
/// </summary>
public enum EditRebuildState
{
    /// <summary>No rebuild needed.</summary>
    Stable,
    /// <summary>A rebuild is recommended but not strictly required.</summary>
    RebuildSuggested,
    /// <summary>A rebuild is required before the document can be used.</summary>
    RebuildRequired,
}

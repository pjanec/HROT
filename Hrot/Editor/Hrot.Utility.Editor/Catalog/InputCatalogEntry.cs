namespace Hrot.Utility.Editor.Catalog;

/// <summary>
/// Metadata about a single Utility AI input accessor (a method on the In.* partial class).
/// Populated by InputCatalogBrowser from reflection over loaded assemblies.
/// </summary>
public sealed class InputCatalogEntry
{
    /// <summary>
    /// Accessor name as it appears in In.* calls (e.g., "HealthFraction", "EqsTopScore").
    /// Matches the value of [UtilityInput] when present.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Grouping label for the picker UI, inferred from [UtilityInput].Name or "Standard".
    /// </summary>
    public string Category { get; }

    /// <summary>Kind of parameter this input takes, if any.</summary>
    public InputParamKind ParameterKind { get; }

    public InputCatalogEntry(string name, string category, InputParamKind parameterKind)
    {
        Name          = name;
        Category      = category;
        ParameterKind = parameterKind;
    }
}

/// <summary>
/// Describes what additional parameter an In.* accessor requires.
/// </summary>
public enum InputParamKind
{
    /// <summary>No parameter (e.g., In.HealthFraction()).</summary>
    None,
    /// <summary>A string template name (e.g., In.EqsTopScore("CoverQuery")).</summary>
    String,
    /// <summary>A float value (e.g., In.Constant(0.5f)).</summary>
    Float,
    /// <summary>An int index (e.g., a mount index).</summary>
    Int,
}

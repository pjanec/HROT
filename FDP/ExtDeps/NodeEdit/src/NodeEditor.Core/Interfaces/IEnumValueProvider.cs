using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Provides enum value lists for inline editors. Host registers one for
/// each enum type its catalog uses.
/// </summary>
public interface IEnumValueProvider
{
    /// <summary>Get values for an enum type.</summary>
    IReadOnlyList<EnumValueEntry> GetValues(TypeKey enumType);

    /// <summary>
    /// Above this count, enum editors fall back from inline combo to picker.
    /// Default 8.
    /// </summary>
    int GetMaxInlineValues();
}

/// <summary>One enum value with display info.</summary>
public sealed record EnumValueEntry(
    long Value,
    string DisplayName,
    string? Description,
    string? IconKey);

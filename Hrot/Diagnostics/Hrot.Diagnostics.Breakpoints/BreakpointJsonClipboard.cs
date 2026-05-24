using System;
using System.Text.Json;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Serializes and deserializes <see cref="SearchPredicateDto"/> to/from JSON
/// for clipboard copy/paste in the Data Breakpoint Manager window.
/// Uses the polymorphic [JsonDerivedType] attributes already on <see cref="SearchPredicateDto"/>.
/// </summary>
public static class BreakpointJsonClipboard
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    /// <summary>Serializes <paramref name="dto"/> to a JSON string.</summary>
    public static string Serialize(SearchPredicateDto dto)
        => JsonSerializer.Serialize<SearchPredicateDto>(dto, _options);

    /// <summary>
    /// Attempts to deserialize a JSON string back to a <see cref="SearchPredicateDto"/>.
    /// Returns <c>null</c> on any parse or type error.
    /// </summary>
    public static SearchPredicateDto? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SearchPredicateDto>(json, _options);
        }
        catch
        {
            return null;
        }
    }
}

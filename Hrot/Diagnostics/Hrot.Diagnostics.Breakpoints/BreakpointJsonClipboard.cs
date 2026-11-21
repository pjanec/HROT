using System;
using System.Text.Json;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Core.Serialization;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Serializes and deserializes <see cref="SearchPredicateDto"/> to/from JSON
/// for clipboard copy/paste in the Data Breakpoint Manager window.
/// Uses the polymorphic [JsonDerivedType] attributes already on <see cref="SearchPredicateDto"/>.
/// </summary>
public static class BreakpointJsonClipboard
{
    // CE-3187 (R-2005 / R-2007): the shared options — enums as names, vectors as arrays. A file saved by an older
    // build with numeric enums no longer loads and is reset.
    private static readonly JsonSerializerOptions _options = FdpJsonOptionsRegistry.Indented;

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

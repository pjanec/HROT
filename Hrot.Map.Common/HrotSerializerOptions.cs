using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.Map.Common;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for HROT scenario file serialisation
/// and deserialisation.
///
/// <para>
/// Design constraints:
/// <list type="bullet">
///   <item>No <c>[JsonPropertyName]</c> attributes on any DTO — policy-based naming
///         via <see cref="JsonNamingPolicy.CamelCase"/> instead.</item>
///   <item>Case-insensitive to tolerate legacy PascalCase files.</item>
///   <item>Null properties are omitted from output to keep files concise.</item>
///   <item>Human-readable indentation for source-control friendliness.</item>
/// </list>
/// </para>
/// </summary>
public static class HrotSerializerOptions
{
    /// <summary>
    /// Pre-built <see cref="JsonSerializerOptions"/> instance for scenario DTO round-trips.
    /// </summary>
    public static readonly JsonSerializerOptions HrotJsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive  = true,
        PropertyNamingPolicy         = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented                = true,
    };
}

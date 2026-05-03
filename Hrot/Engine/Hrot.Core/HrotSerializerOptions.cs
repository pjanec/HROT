using System.Text.Json;
using Fdp.Core.Serialization;

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
/// <para>
/// Built on top of <see cref="FdpJsonOptionsRegistry.Indented"/> (which provides
/// <c>IncludeFields</c>, custom converters and strict enum parsing) with the
/// camelCase naming policy layered on top for scenario file compatibility.
/// </para>
/// </summary>
public static class HrotSerializerOptions
{
    /// <summary>
    /// Pre-built <see cref="JsonSerializerOptions"/> instance for scenario DTO round-trips.
    /// </summary>
    public static readonly JsonSerializerOptions HrotJsonOptions;

    static HrotSerializerOptions()
    {
        // Base on registry Indented options (frozen), then extend with camelCase policy.
        // A new (non-frozen) instance is required to add PropertyNamingPolicy.
        var opts = new JsonSerializerOptions(FdpJsonOptionsRegistry.Indented)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        HrotJsonOptions = opts;
    }
}

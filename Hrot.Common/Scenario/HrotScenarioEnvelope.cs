using System;
using System.Text.Json.Nodes;

namespace Hrot.Common.Scenario;

/// <summary>
/// Application-layer helper that knows the Hrot scenario file envelope format:
/// <c>{ "Header": { "SubsystemType": "...", "SchemaVersion": 1 }, "Entities": { ... } }</c>.
///
/// <para>Moved here from <c>FDP.Toolkit.Scenario.ScenarioSerializer</c> to keep
/// the FDP engine toolkit free of application-layer format knowledge.</para>
/// </summary>
public static class HrotScenarioEnvelope
{
    /// <summary>
    /// Parses the <c>Header.SubsystemType</c> value from raw scenario JSON text
    /// without a full DOM parse. Returns <see langword="null"/> on failure.
    /// </summary>
    public static string? PeekSubsystemType(string jsonText)
    {
        try
        {
            var node = JsonNode.Parse(jsonText);
            return node?["Header"]?["SubsystemType"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="subsystemType"/> matches
    /// <paramref name="expected"/> (ordinal, case-sensitive).
    /// </summary>
    public static bool IsMatchingSubsystem(string? subsystemType, string expected)
        => string.Equals(subsystemType, expected, StringComparison.Ordinal);
}

using System;
using System.Text.Json.Nodes;

namespace Hrot.Common.Scenario;

/// <summary>
/// Application-layer helper that knows the Hrot scenario file envelope format.
/// Supports both the Phase 2 format (<c>$meta.docType</c>) and the legacy format
/// (<c>Header.SubsystemType</c>).
///
/// <para>Moved here from <c>FDP.Toolkit.Scenario.ScenarioSerializer</c> to keep
/// the FDP engine toolkit free of application-layer format knowledge.</para>
/// </summary>
public static class HrotScenarioEnvelope
{
    /// <summary>
    /// Parses the subsystem type from raw scenario JSON text without a full DOM parse.
    /// Returns <see langword="null"/> on failure.
    ///
    /// <para>
    /// Phase 2 format: reads <c>$meta.docType</c>.<br/>
    /// Legacy format: reads <c>Header.SubsystemType</c> (Pascal or camelCase).
    /// </para>
    /// </summary>
    public static string? PeekSubsystemType(string jsonText)
    {
        try
        {
            var node = JsonNode.Parse(jsonText);
            // Phase 2: check $meta.docType first.
            var meta = node?["$meta"] as JsonObject;
            if (meta != null)
                return meta["docType"]?.GetValue<string>();
            // Legacy path: check Header.SubsystemType (Pascal or camelCase).
            return node?["Header"]?["SubsystemType"]?.GetValue<string>()
                ?? node?["header"]?["SubsystemType"]?.GetValue<string>()
                ?? node?["header"]?["subsystemType"]?.GetValue<string>();
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

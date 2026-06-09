using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Serializable DTO for a persisted watch entry (legacy — only saves IsWatch-flagged entries).
/// Superseded by <see cref="DebugSessionPersistence"/> and the new public <see cref="WatchEntry"/>
/// which carries full asset/graph/pin identity for restore.
/// </summary>
[Obsolete("Use DebugSessionPersistence instead.")]
internal sealed class WatchPersistenceEntry
{
    public string DisplayName { get; set; } = string.Empty;
    public SearchPredicateDto? Condition { get; set; }
}

/// <summary>
/// Saves and loads watch entries to/from a JSON file.
/// Superseded by <see cref="DebugSessionPersistence"/> which saves the full debug session.
/// </summary>
[Obsolete("Use DebugSessionPersistence for the full debug session (node BPs + data BPs + watches).")]
public static class WatchPersistence
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    /// <summary>
    /// Serializes all watch-flagged breakpoints to <paramref name="path"/>.
    /// Creates or overwrites the file.
    /// </summary>
    [Obsolete("Use DebugSessionPersistence.Save instead.")]
    public static void Save(IReadOnlyList<Breakpoint> breakpoints, string path)
    {
        var entries = new List<WatchPersistenceEntry>();
        foreach (var bp in breakpoints)
        {
            if (!bp.IsWatch) continue;
            entries.Add(new WatchPersistenceEntry
            {
                DisplayName = bp.DisplayName,
                Condition   = bp.Condition,
            });
        }
        var json = JsonSerializer.Serialize(entries, s_options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Deserializes watch entries from <paramref name="path"/>.
    /// Returns an empty list if the file does not exist or is malformed.
    /// </summary>
    [Obsolete("Use DebugSessionPersistence.TryLoad instead.")]
    internal static IReadOnlyList<WatchPersistenceEntry> TryLoad(string path)
    {
        if (!File.Exists(path)) return Array.Empty<WatchPersistenceEntry>();

        try
        {
            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<List<WatchPersistenceEntry>>(json, s_options);
            return result is not null ? result : Array.Empty<WatchPersistenceEntry>();
        }
        catch
        {
            return Array.Empty<WatchPersistenceEntry>();
        }
    }
}

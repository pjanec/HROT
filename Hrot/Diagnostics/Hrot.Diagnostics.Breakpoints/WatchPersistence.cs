using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Serializable DTO for a persisted watch entry.
/// </summary>
internal sealed class WatchEntry
{
    public string DisplayName { get; set; } = string.Empty;
    public SearchPredicateDto? Condition { get; set; }
}

/// <summary>
/// Saves and loads watch entries to/from a JSON file.
/// </summary>
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
    public static void Save(IReadOnlyList<Breakpoint> breakpoints, string path)
    {
        var entries = new List<WatchEntry>();
        foreach (var bp in breakpoints)
        {
            if (!bp.IsWatch) continue;
            entries.Add(new WatchEntry
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
    internal static IReadOnlyList<WatchEntry> TryLoad(string path)
    {
        if (!File.Exists(path)) return Array.Empty<WatchEntry>();

        try
        {
            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<List<WatchEntry>>(json, s_options);
            return result is not null ? result : Array.Empty<WatchEntry>();
        }
        catch
        {
            return Array.Empty<WatchEntry>();
        }
    }
}

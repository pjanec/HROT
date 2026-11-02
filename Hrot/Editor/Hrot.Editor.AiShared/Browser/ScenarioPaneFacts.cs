using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐ CE-4141 (AQ108 D25) — what the scenario picker's side pane shows about a scenario beyond its name: facts READ FROM <c>scenario.json</c> — the header's description, terrain and
/// subsystem, and how many entities the file holds. Only what the file has: an absent field is <see langword="null"/>, never a placeholder (no objectives / triggers section exists in the
/// file today). Read lazily on first ask and cached per path, re-checked against the file's write time at most every <see cref="RecheckMilliseconds"/> — never parsed per frame.
/// </summary>
public static class ScenarioPaneFacts
{
    /// <summary>How long a cached answer is trusted before the file's write time is looked at again (ms).</summary>
    public const int RecheckMilliseconds = 2000;

    /// <summary>The facts of one scenario file.</summary>
    public sealed record Facts(string? Description, string? TerrainName, string? SubsystemType, int EntityCount);

    private sealed record Cached(DateTime Written, long CheckedAt, Facts? Facts);
    private static readonly Dictionary<string, Cached> Cache = new(StringComparer.Ordinal);

    /// <summary>How many times a file was actually parsed — a probe for the rails.</summary>
    public static int Parses { get; private set; }

    /// <summary>The facts of <paramref name="scenarioJsonPath"/>; <see langword="null"/> when the file is missing or unreadable.</summary>
    public static Facts? Get(string scenarioJsonPath, long? nowMs = null)
    {
        if (string.IsNullOrEmpty(scenarioJsonPath)) return null;
        long now = nowMs ?? Environment.TickCount64;
        lock (Cache)
        {
            if (Cache.TryGetValue(scenarioJsonPath, out var hit) && now - hit.CheckedAt < RecheckMilliseconds) return hit.Facts;
            DateTime written;
            try { written = File.GetLastWriteTimeUtc(scenarioJsonPath); } catch (Exception) { return null; }
            if (hit != null && hit.Written == written) { Cache[scenarioJsonPath] = hit with { CheckedAt = now }; return hit.Facts; }
            var facts = Read(scenarioJsonPath);
            Cache[scenarioJsonPath] = new Cached(written, now, facts);
            return facts;
        }
    }

    private static Facts? Read(string path)
    {
        try
        {
            Parses++;
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            string? desc = null, terrain = null, subsystem = null;
            if (root.TryGetProperty("header", out var h) && h.ValueKind == JsonValueKind.Object)
            {
                desc = Text(h, "description"); terrain = Text(h, "terrainName"); subsystem = Text(h, "subsystemType");
            }
            int entities = root.TryGetProperty("entities", out var e) && e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Count() : 0;
            return new Facts(desc, terrain, subsystem, entities);
        }
        catch (Exception) { return null; }   // unreadable ⇒ no facts, the pane shows the name alone
    }

    private static string? Text(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static int Count(this JsonElement.ObjectEnumerator e) { int n = 0; foreach (var _ in e) n++; return n; }

    /// <summary>The preview picture of a scenario folder (<c>preview.png</c> beside <c>scenario.json</c>) when it exists; else <see langword="null"/>.</summary>
    public static string? PreviewOf(string scenarioJsonPath)
    {
        if (string.IsNullOrEmpty(scenarioJsonPath)) return null;
        try
        {
            var dir = Path.GetDirectoryName(scenarioJsonPath);
            var png = dir == null ? null : Path.Combine(dir, "preview.png");
            return png != null && File.Exists(png) ? png : null;
        }
        catch (Exception) { return null; }
    }
}

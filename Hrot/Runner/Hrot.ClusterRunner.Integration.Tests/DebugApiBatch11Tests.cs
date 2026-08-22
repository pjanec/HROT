using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-11 Tier-1 tests — Group J (logs) and Group B+ (entity filter/spatial).
///
/// <para>Threading: all calls to <see cref="DebugApiService"/> methods run on the
/// test (single) thread, matching the offline harness single-threaded model.</para>
///
/// <para>Log sinks are injected via the <see cref="DebugApiService"/> constructor's
/// <c>logSinks</c> parameter so tests don't touch the process-wide
/// <see cref="NLogMessageLogTarget.SharedInstance"/> (which may have leftover entries
/// from other tests in the suite).</para>
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch11Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DebugApiService BuildServiceWithSinks(EditorHarness h, InMemoryLogSink sink)
    {
        var extraction = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(h.Repo, h.EntityMap, h.Serializer);
        var timeFacade = new Hrot.Editor.UI.EditorTimeTransportFacade(h.Preview, h.TimeController, h.Repo);
        return new DebugApiService(
            h.Repo,
            h.EntityMap,
            extraction,
            timeFacade,
            h.Preview,
            h.Editor,
            h.History,
            h.TimeController,
            clusterState: () => Fdp.Toolkit.Orchestration.ClusterState.Idle,
            logSinks: new IMessageLogSource[] { sink });
    }

    // ── Group J — Logs ────────────────────────────────────────────────────────

    /// <summary>
    /// ?level=Info includes Info, Warning, Error, Critical (minimum-level, inclusive);
    /// excludes Trace and Debug.
    /// </summary>
    [Fact]
    public void GetLogs_LevelFilter_MinimumLevelInclusive()
    {
        using var h = new EditorHarness();
        var sink = new InMemoryLogSink();
        sink.Add(LogSeverity.Trace,   "Test.Logger", "trace entry");
        sink.Add(LogSeverity.Debug,   "Test.Logger", "debug entry");
        sink.Add(LogSeverity.Info,    "Test.Logger", "info entry");
        sink.Add(LogSeverity.Warning, "Test.Logger", "warning entry");
        sink.Add(LogSeverity.Error,   "Test.Logger", "error entry");

        var svc = BuildServiceWithSinks(h, sink);

        // ?level=Info → must include Info, Warning, Error; must exclude Trace, Debug.
        var result = svc.GetLogs(level: "Info");
        Assert.IsType<JsonArray>(result);
        var arr = (JsonArray)result;

        var levels = arr.Select(e => e!["level"]!.GetValue<string>()).ToList();
        Assert.Contains("Info",    levels);
        Assert.Contains("Warning", levels);
        Assert.Contains("Error",   levels);
        Assert.DoesNotContain("Trace", levels);
        Assert.DoesNotContain("Debug", levels);

        // ?level=Warning → must include Warning, Error; must exclude Trace, Debug, Info.
        var warnResult = svc.GetLogs(level: "Warning");
        var warnLevels = ((JsonArray)warnResult)
            .Select(e => e!["level"]!.GetValue<string>()).ToList();
        Assert.DoesNotContain("Info",  warnLevels);
        Assert.Contains("Warning",     warnLevels);
        Assert.Contains("Error",       warnLevels);
    }

    /// <summary>
    /// ?logger= filter narrows by case-insensitive substring match on LoggerName.
    /// </summary>
    [Fact]
    public void GetLogs_LoggerFilter_NarrowsBySubstring()
    {
        using var h = new EditorHarness();
        var sink = new InMemoryLogSink();
        sink.Add(LogSeverity.Info, "AI.Behavior.BTree", "btree event");
        sink.Add(LogSeverity.Info, "AI.Behavior.Hsm",   "hsm event");
        sink.Add(LogSeverity.Info, "Sim.Physics",        "physics event");

        var svc = BuildServiceWithSinks(h, sink);
        var result = svc.GetLogs(logger: "AI.Behavior");
        var arr = (JsonArray)result;

        var loggers = arr.Select(e => e!["logger"]!.GetValue<string>()).ToList();
        Assert.Equal(2, arr.Count);
        Assert.All(loggers, l => Assert.Contains("AI.Behavior", l, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ?since= filter excludes entries older than the specified timestamp.
    /// </summary>
    [Fact]
    public void GetLogs_SinceFilter_ExcludesOlderEntries()
    {
        using var h = new EditorHarness();
        var sink = new InMemoryLogSink();
        sink.AddAt(DateTime.UtcNow.AddMinutes(-10), LogSeverity.Info, "Test", "old entry");
        sink.AddAt(DateTime.UtcNow.AddSeconds(-1),  LogSeverity.Info, "Test", "new entry");

        var svc = BuildServiceWithSinks(h, sink);

        // Since 5 min ago → only the recent entry.
        var cutoff = DateTime.UtcNow.AddMinutes(-5).ToString("O");
        var result = svc.GetLogs(since: cutoff);
        var arr = (JsonArray)result;

        Assert.Equal(1, arr.Count);
        Assert.Contains("new entry", arr[0]!["message"]!.GetValue<string>());
    }

    /// <summary>
    /// ?max= bounds the returned count to at most max entries.
    /// </summary>
    [Fact]
    public void GetLogs_MaxFilter_BoundsCount()
    {
        using var h = new EditorHarness();
        var sink = new InMemoryLogSink();
        for (int i = 1; i <= 50; i++)
            sink.Add(LogSeverity.Info, "Test", $"message {i}");

        var svc = BuildServiceWithSinks(h, sink);
        var result = svc.GetLogs(max: 10);
        Assert.IsType<JsonArray>(result);
        Assert.Equal(10, ((JsonArray)result).Count);
    }

    /// <summary>
    /// No filters → all entries returned.
    /// </summary>
    [Fact]
    public void GetLogs_NoFilters_ReturnsAllEntries()
    {
        using var h = new EditorHarness();
        var sink = new InMemoryLogSink();
        for (int i = 1; i <= 5; i++)
            sink.Add(LogSeverity.Info, "Test", $"entry {i}");

        var svc = BuildServiceWithSinks(h, sink);
        var result = svc.GetLogs();
        Assert.IsType<JsonArray>(result);
        Assert.Equal(5, ((JsonArray)result).Count);
    }

    /// <summary>
    /// Combined level + logger filters compose with AND semantics.
    /// </summary>
    [Fact]
    public void GetLogs_CombinedFilter_LevelAndLogger()
    {
        using var h = new EditorHarness();
        var sink = new InMemoryLogSink();
        sink.Add(LogSeverity.Info,    "AI.Behavior", "ai info");
        sink.Add(LogSeverity.Warning, "AI.Behavior", "ai warning");
        sink.Add(LogSeverity.Warning, "Sim.Physics",  "physics warning");
        sink.Add(LogSeverity.Info,    "Sim.Physics",  "physics info");

        var svc = BuildServiceWithSinks(h, sink);

        // level=Warning AND logger=AI.Behavior → only the "ai warning" entry.
        var result = svc.GetLogs(level: "Warning", logger: "AI.Behavior");
        var arr = (JsonArray)result;

        Assert.Equal(1, arr.Count);
        Assert.Equal("Warning",     arr[0]!["level"]!.GetValue<string>());
        Assert.Contains("AI.Behavior", arr[0]!["logger"]!.GetValue<string>());
    }

    /// <summary>
    /// Each returned entry has all required fields: timestamp, level, logger, message.
    /// </summary>
    [Fact]
    public void GetLogs_ReturnsRequiredFields()
    {
        using var h = new EditorHarness();
        var sink = new InMemoryLogSink();
        sink.Add(LogSeverity.Info, "TestLogger", "hello world");

        var svc = BuildServiceWithSinks(h, sink);
        var result = svc.GetLogs();
        var arr = (JsonArray)result;

        Assert.Equal(1, arr.Count);
        var entry = arr[0]!.AsObject();
        Assert.True(entry.ContainsKey("timestamp"), "Entry must have 'timestamp'");
        Assert.True(entry.ContainsKey("level"),     "Entry must have 'level'");
        Assert.True(entry.ContainsKey("logger"),    "Entry must have 'logger'");
        Assert.True(entry.ContainsKey("message"),   "Entry must have 'message'");
        Assert.Equal("Info",       entry["level"]!.GetValue<string>());
        Assert.Equal("TestLogger", entry["logger"]!.GetValue<string>());
        Assert.Equal("hello world", entry["message"]!.GetValue<string>());
        // Timestamp must be parseable as ISO-8601.
        Assert.True(DateTime.TryParse(entry["timestamp"]!.GetValue<string>(), out _),
            "timestamp must be a valid ISO-8601 datetime string");
    }

    // ── Group B+ — Entity filter / spatial ────────────────────────────────────

    /// <summary>
    /// ?component=SimTransform returns only entities that have that component.
    /// </summary>
    [Fact]
    public void ListEntities_ComponentFilter_NarrowsToMatchingEntities()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        const long networkId1 = 90_100L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = networkId1,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(10f, 0f, 20f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId1, out _), 5000),
            "Entity 90100 did not spawn within timeout.");

        // Unfiltered list must include the entity.
        var allEntities = (JsonArray)svc.ListEntities();
        Assert.True(allEntities.Count > 0, "Unfiltered list should be non-empty.");

        // ?component=SimTransform — only entities that carry SimTransform.
        var filtered = (JsonArray)svc.ListEntities(component: "SimTransform");
        Assert.True(filtered.Count > 0,
            "Filtered list should be non-empty — entity 90100 has SimTransform.");

        // Every returned entity must have SimTransform in its components array.
        foreach (var entry in filtered)
        {
            var comps = (JsonArray)entry!["components"]!;
            var compNames = comps.Select(c => c!.GetValue<string>()).ToList();
            Assert.Contains("SimTransform", compNames, StringComparer.OrdinalIgnoreCase);
        }

        // Filtered count must be ≤ unfiltered count.
        Assert.True(filtered.Count <= allEntities.Count,
            "Component-filtered count must not exceed unfiltered count.");

        // Non-existent component → empty list.
        var emptyResult = (JsonArray)svc.ListEntities(component: "NonExistentComponent9999");
        Assert.Empty(emptyResult);
    }

    /// <summary>
    /// ?near=x,y,r returns only entities within radius r of (x,y) using XZ-plane distance.
    /// </summary>
    [Fact]
    public void ListEntities_NearFilter_ReturnsOnlyEntitiesWithinRadius()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Entity at (50, 0, 50) — XZ distance to origin = sqrt(50^2+50^2) ≈ 70.7 < 100 → inside.
        const long inside = 90_110L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = inside,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(50f, 0f, 50f) },
        });

        // Entity at (500, 0, 500) — XZ distance to origin ≈ 707 > 100 → outside.
        const long outside = 90_111L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = outside,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(500f, 0f, 500f) },
        });

        Assert.True(h.PumpUntil(() =>
            h.EntityMap.TryGetEntity(inside, out _) &&
            h.EntityMap.TryGetEntity(outside, out _), 5000),
            "Both entities must spawn within timeout.");

        // ?near=0,0,100 — radius 100m around origin; "y" in near param maps to world Z axis.
        var nearResult = (JsonArray)svc.ListEntities(near: "0,0,100");
        var insideIds = nearResult.Select(e => e!["networkId"]!.GetValue<long>()).ToList();

        Assert.Contains(inside, insideIds);      // entity at distance ≈70.7 is inside
        Assert.DoesNotContain(outside, insideIds); // entity at distance ≈707 is outside
    }

    /// <summary>
    /// ?component= and ?near= filters compose correctly (AND semantics).
    /// </summary>
    [Fact]
    public void ListEntities_ComponentAndNearFilters_Composable()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        const long closeEntityId = 90_120L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = closeEntityId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(5f, 0f, 5f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(closeEntityId, out _), 5000));

        var result = (JsonArray)svc.ListEntities(component: "SimTransform", near: "0,0,50");
        Assert.True(result.Count > 0, "Combined filter should return the close entity.");

        // Every entity must have SimTransform.
        foreach (var entry in result)
        {
            var comps = (JsonArray)entry!["components"]!;
            var compNames = comps.Select(c => c!.GetValue<string>()).ToList();
            Assert.Contains("SimTransform", compNames, StringComparer.OrdinalIgnoreCase);
        }

        // Non-existent component + valid near → empty.
        var emptyResult = (JsonArray)svc.ListEntities(
            component: "NonExistentComponent9999",
            near: "0,0,50");
        Assert.Empty(emptyResult);
    }

    /// <summary>
    /// No filters → existing behavior (all entities returned, unmodified).
    /// </summary>
    [Fact]
    public void ListEntities_NoFilters_ReturnsAllEntities()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        const long entityId = 90_130L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = entityId, OwnerNodeId = 0, InitType = ReliableInitType.None,
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(entityId, out _), 5000));

        var result = (JsonArray)svc.ListEntities();
        Assert.True(result.Count > 0);
    }

    /// <summary>
    /// The component filter is case-insensitive.
    /// </summary>
    [Fact]
    public void ListEntities_ComponentFilter_IsCaseInsensitive()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        const long entityId = 90_140L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = entityId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(1f, 0f, 1f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(entityId, out _), 5000));

        var lower = (JsonArray)svc.ListEntities(component: "simtransform");
        var upper = (JsonArray)svc.ListEntities(component: "SIMTRANSFORM");
        var canon = (JsonArray)svc.ListEntities(component: "SimTransform");

        Assert.Equal(canon.Count, lower.Count);
        Assert.Equal(canon.Count, upper.Count);
    }
}

// ── Test helper ────────────────────────────────────────────────────────────────

/// <summary>
/// In-memory <see cref="IMessageLogSource"/> for injecting test log entries into
/// <see cref="DebugApiService"/> without touching the process-wide NLog singletons.
/// </summary>
internal sealed class InMemoryLogSink : IMessageLogSource
{
    private readonly List<MessageLogEntry> _entries = new();
    private readonly object _lock = new();

    public string SourceId    => "test_sink";
    public string DisplayName => "Test";

    public event Action<MessageLogEntry>? OnMessageAdded;

    public void Add(LogSeverity severity, string logger, string message)
        => AddAt(DateTime.UtcNow, severity, logger, message);

    public void AddAt(DateTime timestamp, LogSeverity severity, string logger, string message)
    {
        var entry = new MessageLogEntry(timestamp, severity, logger, message,
            Array.Empty<LogChunk>());
        lock (_lock)
            _entries.Add(entry);
        OnMessageAdded?.Invoke(entry);
    }

    public IReadOnlyList<MessageLogEntry> GetMessages()
    {
        lock (_lock)
            return _entries.ToArray();
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
    }
}

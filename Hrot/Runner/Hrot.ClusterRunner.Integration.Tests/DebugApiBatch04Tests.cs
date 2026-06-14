using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Hrot.Common.Events;
using Hrot.Editor.Commands;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-04 Tier-1 gate — exercises the new Group F endpoints (commands discovery,
/// components discovery, generic command send, entity spawn) against the offline
/// <see cref="EditorHarness"/>. No HTTP; runs fast.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch04Tests
{
    private const long TestTkbType   = 1L;
    private const long TestNetworkId = 42L;
    private const int  PumpTimeoutMs = 5_000;

    private static void SpawnTestEntity(EditorHarness h, long networkId, Vector3 pos = default)
    {
        var cmd = new SpawnEntityCommand
        {
            TkbType          = TestTkbType,
            NetworkId        = networkId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = pos },
        };
        h.Bus.PublishManaged(cmd);
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), PumpTimeoutMs),
            $"Entity {networkId} did not appear within timeout.");
    }

    // ── GET /commands ────────────────────────────────────────────────────────

    [Fact]
    public void ListCommands_ReturnsNonEmpty_WithFieldSchemas()
    {
        using var h   = new EditorHarness();
        // Force event types to be registered by touching SpawnEntityCommand and MissionControlAckEvent.
        // (In the harness they are registered implicitly via the spawn path.)
        SpawnTestEntity(h, TestNetworkId);

        var svc      = h.BuildDebugApiService();
        var commands = svc.ListCommands().AsArray();
        Assert.NotEmpty(commands);

        // Every entry must have name + fields array.
        foreach (var cmd in commands)
        {
            Assert.NotNull(cmd!["name"]?.GetValue<string>());
            Assert.NotNull(cmd["fields"]);
        }
    }

    [Fact]
    public void ListCommands_IncludesMissionControlAckEvent_WithFieldSchema()
    {
        using var h = new EditorHarness();
        // Force the static EventType<T>.Id for MissionControlAckEvent so it appears in the registry.
        // This mirrors what happens in production when the bus processes ack events.
        _ = EventType<MissionControlAckEvent>.Id;
        // Also force CenterOnEntityCommand.
        _ = EventType<CenterOnEntityCommand>.Id;

        var svc      = h.BuildDebugApiService();
        var commands = svc.ListCommands().AsArray();

        // MissionControlAckEvent is an unmanaged struct with [EventId(6002)] — it must appear.
        var ackEntry = commands
            .Select(n => n!.AsObject())
            .FirstOrDefault(o => o["name"]?.GetValue<string>() == "MissionControlAckEvent");

        Assert.NotNull(ackEntry);
        var fields = ackEntry!["fields"]!.AsArray();
        Assert.NotEmpty(fields);
        // RequestId is a Guid → described as "object" (complex nested).
        var reqIdField = fields.Select(f => f!.AsObject())
            .FirstOrDefault(f => f["name"]?.GetValue<string>() == "RequestId");
        Assert.NotNull(reqIdField);
    }

    [Fact]
    public void ListCommands_IncludesCenterOnEntityCommand()
    {
        using var h = new EditorHarness();
        // Force registration.
        _ = EventType<CenterOnEntityCommand>.Id;

        var svc      = h.BuildDebugApiService();
        var commands = svc.ListCommands().AsArray();

        var entry = commands
            .Select(n => n!.AsObject())
            .FirstOrDefault(o => o["name"]?.GetValue<string>() == "CenterOnEntityCommand");

        Assert.NotNull(entry);
        var fields = entry!["fields"]!.AsArray();
        Assert.NotEmpty(fields);
        // NetworkId is a long → "number".
        var networkIdField = fields.Select(f => f!.AsObject())
            .FirstOrDefault(f => f["name"]?.GetValue<string>() == "NetworkId");
        Assert.NotNull(networkIdField);
        Assert.Equal("number", networkIdField!["type"]?.GetValue<string>());
    }

    // ── GET /components ───────────────────────────────────────────────────────

    [Fact]
    public void ListComponents_ReturnsNonEmpty_WithFieldSchemas()
    {
        using var h   = new EditorHarness();
        var svc       = h.BuildDebugApiService();
        var components = svc.ListComponents().AsArray();
        Assert.NotEmpty(components);

        foreach (var comp in components)
        {
            Assert.NotNull(comp!["name"]?.GetValue<string>());
            Assert.NotNull(comp["fields"]);
        }
    }

    [Fact]
    public void ListComponents_IncludesEntityInfo()
    {
        using var h   = new EditorHarness();
        var svc       = h.BuildDebugApiService();
        var components = svc.ListComponents().AsArray();

        var ei = components
            .Select(n => n!.AsObject())
            .FirstOrDefault(o => o["name"]?.GetValue<string>() == "EntityInfo");

        Assert.NotNull(ei);
    }

    // ── POST /entities/command ────────────────────────────────────────────────

    [Fact]
    public void SendCommand_UnknownEventType_Returns400Error()
    {
        using var h  = new EditorHarness();
        var svc      = h.BuildDebugApiService();

        var (result, error) = svc.SendCommand("NoSuchEvent_XYZ", null, wait: false);
        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("NoSuchEvent_XYZ", error);
    }

    [Fact]
    public void SendCommand_ValidUnmanagedCommand_AppearsInEventHistory()
    {
        using var h  = new EditorHarness();
        // Force registration of CenterOnEntityCommand in EventTypeRegistry.
        _ = EventType<CenterOnEntityCommand>.Id;

        var svc = h.BuildDebugApiService();

        // Send a CenterOnEntityCommand via the generic command endpoint.
        var payload = new JsonObject { ["NetworkId"] = 42L };

        var (result, error) = svc.SendCommand("CenterOnEntityCommand", payload, wait: false);
        Assert.Null(error);
        Assert.NotNull(result);
        Assert.False(result!["awaited"]?.GetValue<bool>());

        // Pump so the event is processed into history.
        h.PumpFrames(2);

        // The event should appear in the world event history.
        var events = svc.GetEvents(bus: "world", type: "CenterOnEntityCommand").AsArray();
        Assert.NotEmpty(events);
    }

    [Fact]
    public void SendCommand_WaitTrue_SimPaused_ReturnsAwaitedFalse_SimNotRunning()
    {
        using var h = new EditorHarness();
        // Force registration of CenterOnEntityCommand.
        _ = EventType<CenterOnEntityCommand>.Id;

        var svc = h.BuildDebugApiService();

        // Harness is paused (deterministic) by default — not in preview.
        Assert.True(svc.GetSimState().AsObject()["isPaused"]?.GetValue<bool>(),
            "Harness should be paused at rest for wait-gating test.");

        var (result, error) = svc.SendCommand("CenterOnEntityCommand", null, wait: true);
        Assert.Null(error);
        Assert.NotNull(result);
        // When sim is not running (not InPreview), wait-gating must return awaited:false.
        Assert.False(result!["awaited"]?.GetValue<bool>(), "awaited should be false when sim not running.");
        // Reason must be present.
        Assert.NotNull(result["reason"]?.GetValue<string>());
    }

    // ── POST /entities/spawn ───────────────────────────────────────────────────

    [Fact]
    public void SpawnEntity_ValidTkbType_IncreasesEntityCount()
    {
        using var h  = new EditorHarness();
        var svc      = h.BuildDebugApiService();

        int countBefore = svc.GetStatus().AsObject()["entityCount"]!.GetValue<int>();

        var result = svc.SpawnEntity(TestTkbType);
        Assert.NotNull(result);
        Assert.True(result["spawned"]?.GetValue<bool>());

        // Pump until entityCount increases.
        bool grew = h.PumpUntil(
            () => svc.GetStatus().AsObject()["entityCount"]!.GetValue<int>() > countBefore,
            PumpTimeoutMs);
        Assert.True(grew, $"entityCount did not increase after spawn (was {countBefore}).");
    }

    [Fact]
    public void SpawnEntity_Paused_ReturnsAwaitedFalse()
    {
        using var h = new EditorHarness();
        var svc     = h.BuildDebugApiService();

        var result = svc.SpawnEntity(TestTkbType);
        Assert.False(result["awaited"]?.GetValue<bool>());
        Assert.NotNull(result["reason"]?.GetValue<string>());
    }

    [Fact]
    public void ListEntities_AfterSpawn_ShowsNewEntity()
    {
        using var h  = new EditorHarness();
        var svc      = h.BuildDebugApiService();

        svc.SpawnEntity(TestTkbType);
        Assert.True(h.PumpUntil(() => svc.ListEntities().AsArray().Count > 0, PumpTimeoutMs),
            "Spawned entity should appear in ListEntities.");
    }
}

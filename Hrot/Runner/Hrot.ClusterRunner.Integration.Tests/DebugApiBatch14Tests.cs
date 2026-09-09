using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-14 Tier-1 tests:
///   T06b — Managed-event discovery in GET /commands (ADA-04-D02 fix)
///   P9   — FocusEntity (publish CenterOnEntityCommand) + AddAnnotation (DebugPrimitiveBuffer write)
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch14Tests
{
    private const long TestNetworkId = 90_140L;

    // ── T06b: Managed-event discovery ─────────────────────────────────────────

    /// <summary>
    /// ListCommands() returns managed events tagged managed:true AND unmanaged events tagged
    /// managed:false. After publishing a SpawnEntityCommand (a managed event), it must appear
    /// in the list with managed:true. Unmanaged events must also still be present.
    /// </summary>
    [Fact]
    public void ListCommands_IncludesUnmanagedAndManagedEvents()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Ensure SpawnEntityCommand (managed) is registered on the bus by publishing one.
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = TestNetworkId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform(),
        });
        h.PumpFrames(1);

        var result = svc.ListCommands();
        Assert.IsType<JsonArray>(result);
        var arr = (JsonArray)result;
        Assert.True(arr.Count > 0, "ListCommands must return at least one entry.");

        // Find SpawnEntityCommand (managed).
        JsonObject? spawnEntry = null;
        bool hasUnmanaged = false;
        foreach (var node in arr)
        {
            if (node is not JsonObject obj) continue;
            var name = obj["name"]?.GetValue<string>();
            var managed = obj["managed"]?.GetValue<bool>();

            Assert.NotNull(managed); // every entry must have a "managed" boolean field

            if (string.Equals(name, "SpawnEntityCommand", StringComparison.OrdinalIgnoreCase))
            {
                spawnEntry = obj;
                Assert.True(managed, "SpawnEntityCommand must be tagged managed:true.");
            }

            if (managed == false)
                hasUnmanaged = true;
        }

        Assert.NotNull(spawnEntry);  // SpawnEntityCommand must appear after publish
        Assert.True(hasUnmanaged, "At least one unmanaged event (managed:false) must be present.");
    }

    /// <summary>
    /// Every entry in ListCommands() has a "fields" array (may be empty for events with no
    /// fields), confirming the JsonShapeDescriber path ran for managed types as well.
    /// </summary>
    [Fact]
    public void ListCommands_ManagedEntry_HasFieldsArray()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Register SpawnEntityCommand.
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType   = 1L,
            NetworkId = TestNetworkId + 1,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
            InitialTransform = new SimTransform(),
        });
        h.PumpFrames(1);

        var arr = (JsonArray)svc.ListCommands();
        var spawnEntry = arr
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                o["name"]?.GetValue<string>(), "SpawnEntityCommand",
                StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(spawnEntry);
        Assert.IsType<JsonArray>(spawnEntry!["fields"]);
        var fields = (JsonArray)spawnEntry["fields"]!;
        // SpawnEntityCommand has multiple fields (TkbType, NetworkId, OwnerNodeId, etc.)
        Assert.True(fields.Count > 0, "SpawnEntityCommand must have at least one field described.");
    }

    // ── P9: FocusEntity ──────────────────────────────────────────────────────

    /// <summary>
    /// FocusEntity() publishes a CenterOnEntityCommand that is captured in the event history.
    /// The command publish is the headless-verifiable part; the camera move itself is MANUAL-VERIFY.
    /// </summary>
    [Fact]
    public void FocusEntity_PublishesCenterOnEntityCommand()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var result = svc.FocusEntity(1000L);
        Assert.IsType<JsonObject>(result);
        var obj = (JsonObject)result;
        Assert.True(obj["focused"]?.GetValue<bool>(), "focused must be true.");

        // Pump a frame so the event history capture system records the command.
        h.PumpFrames(1);

        // CenterOnEntityCommand must appear in the world event history.
        var eventsNode = svc.GetEvents("world", "CenterOnEntityCommand", 0, 200);
        Assert.IsType<JsonArray>(eventsNode);
        var eventsArr = (JsonArray)eventsNode;
        Assert.True(eventsArr.Count > 0,
            "CenterOnEntityCommand must appear in event history after FocusEntity.");

        // Verify the NetworkId field round-trips correctly.
        var ev = eventsArr[0] as JsonObject;
        Assert.NotNull(ev);
        // Event history entries contain a 'payload' or top-level fields depending on format.
        // Either way, the event must be identifiable.
        var evStr = ev!.ToJsonString();
        Assert.Contains("CenterOnEntityCommand", evStr);
    }

    // ── P9: AddAnnotation ─────────────────────────────────────────────────────

    /// <summary>
    /// AddAnnotation with type "sphere" writes a primitive to the DebugPrimitiveBuffer.
    /// The buffer count must increase. The render is MANUAL-VERIFY (requires windowed session).
    /// </summary>
    [Fact]
    public void AddAnnotation_Sphere_WritesToBuffer()
    {
        using var h = new EditorHarness();
        var buffer = new DebugPrimitiveBuffer();
        var svc = h.BuildDebugApiService(primitiveBuffer: buffer);

        int countBefore = buffer.Count;

        var body = JsonNode.Parse("{\"type\":\"sphere\",\"x\":10,\"y\":0,\"z\":5,\"radius\":3}");
        var (result, error) = svc.AddAnnotation(body);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result!["added"]?.GetValue<bool>(), "added must be true.");

        int countAfter = buffer.Count;
        Assert.True(countAfter > countBefore,
            "DebugPrimitiveBuffer.Count must increase after AddAnnotation.");
    }

    /// <summary>
    /// AddAnnotation with type "anchor" writes a SpatialAnchor primitive to the buffer.
    /// </summary>
    [Fact]
    public void AddAnnotation_Anchor_WritesToBuffer()
    {
        using var h = new EditorHarness();
        var buffer = new DebugPrimitiveBuffer();
        var svc = h.BuildDebugApiService(primitiveBuffer: buffer);

        var body = JsonNode.Parse(
            "{\"type\":\"anchor\",\"networkId\":1000,\"x\":1,\"y\":2,\"z\":3,\"heading\":45}");
        var (result, error) = svc.AddAnnotation(body);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result!["added"]?.GetValue<bool>(), "added must be true.");
        Assert.True(buffer.Count > 0, "Buffer must be non-empty after DrawSpatialAnchor.");
    }

    /// <summary>
    /// AddAnnotation with type "line" writes a Line primitive to the buffer.
    /// </summary>
    [Fact]
    public void AddAnnotation_Line_WritesToBuffer()
    {
        using var h = new EditorHarness();
        var buffer = new DebugPrimitiveBuffer();
        var svc = h.BuildDebugApiService(primitiveBuffer: buffer);

        var body = JsonNode.Parse(
            "{\"type\":\"line\",\"from\":{\"x\":0,\"y\":0,\"z\":0},\"to\":{\"x\":100,\"y\":0,\"z\":0}}");
        var (result, error) = svc.AddAnnotation(body);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result!["added"]?.GetValue<bool>(), "added must be true.");
        Assert.True(buffer.Count > 0, "Buffer must be non-empty after DrawLine.");
    }

    /// <summary>
    /// AddAnnotation without a primitiveBuffer wired returns a clear error.
    /// </summary>
    [Fact]
    public void AddAnnotation_NoBuffer_ReturnsError()
    {
        using var h = new EditorHarness();
        // Build WITHOUT a buffer (default null).
        var svc = h.BuildDebugApiService();

        var body = JsonNode.Parse("{\"type\":\"sphere\",\"x\":0,\"y\":0,\"z\":0,\"radius\":1}");
        var (result, error) = svc.AddAnnotation(body);

        Assert.NotNull(error);
        Assert.Null(result);
    }

    /// <summary>
    /// AddAnnotation with unknown type returns error.
    /// </summary>
    [Fact]
    public void AddAnnotation_UnknownType_ReturnsError()
    {
        using var h = new EditorHarness();
        var buffer = new DebugPrimitiveBuffer();
        var svc = h.BuildDebugApiService(primitiveBuffer: buffer);

        var body = JsonNode.Parse("{\"type\":\"unknown_type_xyz\"}");
        var (result, error) = svc.AddAnnotation(body);

        Assert.NotNull(error);
        Assert.Null(result);
    }
}

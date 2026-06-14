using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-09 Tier-1 gate — verifies that NaN/Infinity float values in entity components
/// are serialized as string sentinels (<c>"NaN"</c>, <c>"Infinity"</c>, <c>"-Infinity"</c>)
/// rather than named float literals, so the output is valid standard JSON accepted by
/// <c>JSON.parse</c> (Node MCP client).
///
/// <para>
/// Root cause (ADA-08-D02): the entity dump path used
/// <c>JsonNode.Parse(JsonSerializer.Serialize(dump, DefaultRelaxed))</c>; the VectorArrayConverters
/// emit raw NaN literals via <c>WriteRawValue</c> and <c>JsonNode.Parse</c> rejects them.
/// </para>
/// <para>
/// Fix: <see cref="DebugApiService.DebugApiDumpOptions"/> is a scoped
/// <see cref="JsonSerializerOptions"/> with NaN-safe float/vector converters; <c>DumpToJsonNode</c>
/// uses <c>JsonSerializer.SerializeToNode(dump, DebugApiDumpOptions)</c> — no string round-trip.
/// </para>
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch09Tests
{
    // ── NaN-safe serialization unit tests ────────────────────────────────────────

    /// <summary>
    /// A <see cref="DebugApiService"/> built with a no-serializer extraction service uses the
    /// raw-struct fallback path.  Setting <c>SimTransform.Position = Vector3(NaN, ...)</c> and
    /// then calling <c>DumpEntity</c> must return a JSON node with the string sentinel
    /// <c>"NaN"</c> instead of throwing or emitting an invalid literal.
    /// </summary>
    [Fact]
    public void DumpEntity_NaNPosition_ReturnsSentinelString_NotException()
    {
        using var h = new EditorHarness();

        // Build the DebugApiService with a no-serializer extraction service so the
        // fallback (raw-struct) path is exercised.  The fallback path stores the raw
        // SimTransform struct in Components; DumpToJsonNode serializes it via
        // DebugApiDumpOptions which has the NaN-safe Vector3 converter.
        var extractionNoSerializer = new EntityStateExtractionService(h.Repo, h.EntityMap);
        var timeFacade = new Hrot.Editor.UI.EditorTimeTransportFacade(h.Preview, h.TimeController, h.Repo);
        var svc = new DebugApiService(
            h.Repo,
            h.EntityMap,
            extractionNoSerializer,
            timeFacade,
            h.Preview,
            h.Editor,
            h.History,
            h.TimeController,
            clusterState: () => Fdp.Toolkit.Orchestration.ClusterState.Idle);

        // Spawn entity via real pipeline so it gets NetworkIdentity.
        const long networkId = 90_001L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = networkId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(1f, 2f, 3f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), 5000),
            "Entity did not spawn within timeout.");

        // Set a NaN position directly on the component.
        Assert.True(h.EntityMap.TryGetEntity(networkId, out var entity));
        h.Repo.SetComponent(entity, new SimTransform { Position = new Vector3(float.NaN, 0f, 0f) });

        // DumpEntity must NOT throw and must return ok JSON with "NaN" string sentinel.
        JsonNode? dump = null;
        var ex = Record.Exception(() => dump = svc.DumpEntity(networkId));
        Assert.Null(ex); // must not throw
        Assert.NotNull(dump);

        // Serialize to string and verify it is valid standard JSON (no named literals).
        var json = dump!.ToJsonString();
        Assert.False(string.IsNullOrWhiteSpace(json));

        // The JSON must be parseable without error (standard JSON.parse compliance).
        Assert.True(TryParseStrictJson(json, out _), $"DumpEntity output is not valid standard JSON:\n{json}");

        // The NaN sentinel must appear as a JSON string "NaN", not a bare literal.
        Assert.Contains("\"NaN\"", json);
        // Bare literal NaN (without quotes) must NOT appear.
        Assert.DoesNotContain(":NaN", json);
        Assert.DoesNotContain(",NaN", json);
        Assert.DoesNotContain("[NaN", json);
    }

    /// <summary>
    /// Positive/negative Infinity sentinels must also be emitted as JSON strings.
    /// </summary>
    [Fact]
    public void DumpEntity_InfinityPosition_ReturnsSentinelString()
    {
        using var h = new EditorHarness();

        var extractionNoSerializer = new EntityStateExtractionService(h.Repo, h.EntityMap);
        var timeFacade = new Hrot.Editor.UI.EditorTimeTransportFacade(h.Preview, h.TimeController, h.Repo);
        var svc = new DebugApiService(
            h.Repo,
            h.EntityMap,
            extractionNoSerializer,
            timeFacade,
            h.Preview,
            h.Editor,
            h.History,
            h.TimeController,
            clusterState: () => Fdp.Toolkit.Orchestration.ClusterState.Idle);

        const long networkId = 90_002L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = networkId, OwnerNodeId = 0,
            InitType = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(0f, float.PositiveInfinity, 0f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), 5000));

        Assert.True(h.EntityMap.TryGetEntity(networkId, out var entity));
        h.Repo.SetComponent(entity, new SimTransform { Position = new Vector3(0f, float.PositiveInfinity, float.NegativeInfinity) });

        JsonNode? dump = null;
        var ex = Record.Exception(() => dump = svc.DumpEntity(networkId));
        Assert.Null(ex);
        Assert.NotNull(dump);

        var json = dump!.ToJsonString();
        Assert.True(TryParseStrictJson(json, out _), $"DumpEntity output not valid JSON:\n{json}");
        Assert.Contains("\"Infinity\"", json);
        Assert.Contains("\"-Infinity\"", json);
    }

    /// <summary>
    /// <c>DumpEntity</c> with NaN component round-trips through <c>CaptureBaseline</c> +
    /// <c>CompareBaseline</c> (diff surface) without throwing.
    /// </summary>
    [Fact]
    public void Diff_NaNEntity_ReturnsValidJson()
    {
        using var h = new EditorHarness();

        var extractionNoSerializer = new EntityStateExtractionService(h.Repo, h.EntityMap);
        var timeFacade = new Hrot.Editor.UI.EditorTimeTransportFacade(h.Preview, h.TimeController, h.Repo);
        var svc = new DebugApiService(
            h.Repo,
            h.EntityMap,
            extractionNoSerializer,
            timeFacade,
            h.Preview,
            h.Editor,
            h.History,
            h.TimeController,
            clusterState: () => Fdp.Toolkit.Orchestration.ClusterState.Idle);

        const long networkId = 90_003L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = networkId, OwnerNodeId = 0,
            InitType = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new Vector3(1f, 0f, 0f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), 5000));

        // Capture baseline with finite value.
        var captureResult = svc.CaptureBaseline(new[] { networkId });
        var baselineId = captureResult["baselineId"]!.GetValue<string>();

        // Set NaN after baseline capture.
        Assert.True(h.EntityMap.TryGetEntity(networkId, out var entity));
        h.Repo.SetComponent(entity, new SimTransform { Position = new Vector3(float.NaN, 0f, 0f) });
        h.PumpFrames(1);

        // CompareBaseline must not throw and must return valid JSON.
        JsonNode? diffResult = null;
        var ex = Record.Exception(() => diffResult = svc.CompareBaseline(baselineId, new[] { networkId }));
        Assert.Null(ex);
        Assert.NotNull(diffResult);

        var json = diffResult!.ToJsonString();
        Assert.True(TryParseStrictJson(json, out _), $"diff output is not valid standard JSON:\n{json}");
    }

    /// <summary>
    /// <c>DebugApiService.DebugApiDumpOptions</c> is internal and accessible from tests
    /// (InternalsVisibleTo). This test directly validates the scoped options produce
    /// valid JSON for an <see cref="EntityStateDumpDto"/> with NaN floats in a raw struct.
    /// </summary>
    [Fact]
    public void DebugApiDumpOptions_NaNSimTransform_ProducesValidJson()
    {
        // Build a raw EntityStateDumpDto with a SimTransform carrying NaN position.
        // This mirrors the fallback path in EntityStateExtractionService.
        var nanTransform = new SimTransform { Position = new Vector3(float.NaN, float.PositiveInfinity, float.NegativeInfinity) };
        var dump = new EntityStateDumpDto
        {
            EntityId   = new[] { 0, 0 },
            NetworkId  = 1L,
            Components = new Dictionary<string, object>
            {
                ["SimTransform"] = nanTransform,
            },
        };

        // SerializeToNode must not throw and produce valid JSON.
        JsonNode? node = null;
        var ex = Record.Exception(() =>
            node = JsonSerializer.SerializeToNode(dump, DebugApiService.DebugApiDumpOptions));
        Assert.Null(ex);
        Assert.NotNull(node);

        var json = node!.ToJsonString();
        Assert.True(TryParseStrictJson(json, out _), $"DebugApiDumpOptions output not valid JSON:\n{json}");

        // All three sentinels must appear.
        Assert.Contains("\"NaN\"",       json);
        Assert.Contains("\"Infinity\"",  json);
        Assert.Contains("\"-Infinity\"", json);

        // Bare literals must NOT appear.
        Assert.DoesNotContain(":NaN",  json);
        Assert.DoesNotContain("[NaN",  json);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse <paramref name="json"/> with strict settings
    /// (<c>AllowNamedFloatingPointLiterals = false</c>, which is the default for
    /// <c>JsonDocument</c>) to verify the output is RFC-8259-compliant.
    /// </summary>
    private static bool TryParseStrictJson(string json, out JsonDocument? doc)
    {
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling     = JsonCommentHandling.Disallow,
            });
            return true;
        }
        catch (JsonException)
        {
            doc = null;
            return false;
        }
    }
}

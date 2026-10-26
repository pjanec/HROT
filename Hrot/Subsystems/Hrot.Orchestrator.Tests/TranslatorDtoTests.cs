using System;
using System.Text.Json;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for CMC-S011: JSON payload DTO round-tripping.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class TranslatorDtoTests
{
    private static readonly JsonSerializerOptions Options = OrchestrationJsonOptions.Default;

    // ── Test 1: Valid enum string deserialisation ─────────────────────────────

    [Fact]
    public void TransitionPayloadDto_DeserializesEnumString_Correctly()
    {
        const string json = "{\"TargetState\":\"OperatingLive\", \"ScenarioId\":\"Test\"}";

        var dto = JsonSerializer.Deserialize<TransitionPayloadDto>(json, Options)!;

        Assert.Equal(ClusterState.OperatingLive, dto.TargetState);
        Assert.Equal("Test", dto.ScenarioId);
    }

    // ── Test 2: Integer enum should throw ─────────────────────────────────────

    [Fact]
    public void TransitionPayloadDto_ThrowsOnIntegerEnum()
    {
        const string json = "{\"TargetState\": 31}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TransitionPayloadDto>(json, Options));
    }

    // ── Test 3: WhenWritingNull suppresses null properties ────────────────────

    [Fact]
    public void TransitionPayloadDto_SerializesWithNullSuppression()
    {
        var dto  = new TransitionPayloadDto(ClusterState.LoadingLive, null, Guid.Empty, null);
        var json = JsonSerializer.Serialize(dto, Options);

        Assert.Contains("\"TargetState\"", json);
        Assert.DoesNotContain("\"ScenarioId\"", json);
        Assert.Contains("\"ExerciseId\"", json);
        Assert.DoesNotContain("\"TimeMode\"", json);
    }

    /// <summary>⭐ R-3055 — a transition crosses the wire WHOLE: the one encoding and the one decoding keep every field an intent carries.
    /// 📌 Measured on the five-process cluster: POST /replay {atSeconds:30} sent from CGF entered at 0 — TargetWallTicks was not on the
    /// wire and three hand-written decoders each dropped some fields (two lost CheckpointingSeconds too).</summary>
    [Fact]
    public void ATransition_CrossesTheWireWhole()
    {
        var sent = new Fdp.Toolkit.Orchestration.TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(), TargetState = Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay, ScenarioId = "s",
            ExerciseId = Guid.NewGuid(), TimeMode = "Deterministic", CheckpointingSeconds = 5, TargetWallTicks = TimeSpan.FromSeconds(30).Ticks,
            TargetSimSeconds = 12.5,   // ⭐ CE-3329 R11
        };

        var json = JsonSerializer.Serialize(TransitionPayloadDto.From(sent), Options);
        var got  = JsonSerializer.Deserialize<TransitionPayloadDto>(json, Options)!.ToIntent(sent.TransactionId);

        Assert.Equal(sent, got);
    }

    /// <summary>⭐ CE-3329 — a seek crosses the wire whole (its sim target included), and a bare tick count from an older sender still
    /// decodes; the axis switch round-trips, an unreadable one is Sim (the default).</summary>
    [Fact]
    public void A_seek_and_an_axis_switch_cross_the_wire_whole()
    {
        var seek = new Fdp.Toolkit.Orchestration.SeekReplayIntent { RequestId = Guid.NewGuid(), TargetWallTicks = 42, TargetSimSeconds = 7.25 };
        var json = JsonSerializer.Serialize(SeekReplayPayloadDto.From(seek), Options);
        Assert.Equal(seek, SeekReplayPayloadDto.Decode(seek.RequestId, json));
        Assert.Equal(99L, SeekReplayPayloadDto.Decode(Guid.Empty, "99").TargetWallTicks);

        var axis = new Fdp.Toolkit.Orchestration.SetReplayAxisIntent { RequestId = Guid.NewGuid(), Axis = Fdp.Toolkit.Orchestration.ReplayAxis.Wall };
        Assert.Equal(axis, SetReplayAxisPayloadDto.Decode(axis.RequestId, JsonSerializer.Serialize(SetReplayAxisPayloadDto.From(axis), Options)));
        Assert.Equal(Fdp.Toolkit.Orchestration.ReplayAxis.Sim, SetReplayAxisPayloadDto.Decode(Guid.Empty, "{\"Axis\":\"sideways\"}").Axis);
    }

    // ── Test 4: Unknown enum string should throw ──────────────────────────────

    [Fact]
    public void TransitionPayloadDto_ThrowsOnUnknownEnumString()
    {
        const string json = "{\"TargetState\": \"OperatingLive_V2\"}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TransitionPayloadDto>(json, Options));
    }

    // ── D2: purpose-built zone / terrain-build payloads ───────────────────────

    /// <summary>
    /// ⭐ D2 — a zone id is a NAME, not a <see cref="Guid"/>.
    /// ⛔ The regression this pins: the <c>LoadZone</c> arm used to deserialize
    /// <c>ArchivePayloadDto</c> and stringify its <c>ExerciseId</c> into the zone id, which silently
    /// constrained zone ids to GUID shape and named a different domain.
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.3.
    /// </summary>
    [Fact]
    public void ZonePayloadDto_RoundTripsANonGuidZoneId()
    {
        var json = JsonSerializer.Serialize(new ZonePayloadDto("north-ridge"), Options);
        Assert.Contains("\"ZoneId\"", json);

        var dto = JsonSerializer.Deserialize<ZonePayloadDto>(json, Options)!;
        Assert.Equal("north-ridge", dto.ZoneId);

        // ⛔ And the old shape does NOT smuggle a zone id through: an ArchivePayloadDto-shaped
        //    payload carries no ZoneId at all, which is the point of the split.
        var fromArchiveShape = JsonSerializer.Deserialize<ZonePayloadDto>(
            "{\"ExerciseId\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"}", Options)!;
        Assert.Null(fromArchiveShape.ZoneId);
    }

    /// <summary>
    /// ⭐ D2 — the build op carries a KIND list; null means "all" and must survive the trip as null
    /// rather than becoming an empty array that reads as "nothing to build".
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3.1.
    /// </summary>
    [Fact]
    public void TerrainAssetBuildPayloadDto_RoundTripsKinds_AndKeepsNullDistinctFromEmpty()
    {
        var withKinds = JsonSerializer.Deserialize<TerrainAssetBuildPayloadDto>(
            JsonSerializer.Serialize(new TerrainAssetBuildPayloadDto(new[] { "roads", "tiles" }), Options),
            Options)!;
        Assert.Equal(new[] { "roads", "tiles" }, withKinds.Kinds);

        var allKinds = JsonSerializer.Deserialize<TerrainAssetBuildPayloadDto>("{}", Options)!;
        Assert.Null(allKinds.Kinds);

        var noKinds = JsonSerializer.Deserialize<TerrainAssetBuildPayloadDto>("{\"Kinds\":[]}", Options)!;
        Assert.NotNull(noKinds.Kinds);
        Assert.Empty(noKinds.Kinds!);
    }
}

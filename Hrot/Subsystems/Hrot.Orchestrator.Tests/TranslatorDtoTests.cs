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

    // ── Test 4: Unknown enum string should throw ──────────────────────────────

    [Fact]
    public void TransitionPayloadDto_ThrowsOnUnknownEnumString()
    {
        const string json = "{\"TargetState\": \"OperatingLive_V2\"}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TransitionPayloadDto>(json, Options));
    }
}

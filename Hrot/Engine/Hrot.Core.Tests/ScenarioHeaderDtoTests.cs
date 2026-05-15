using System.Text.Json;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Xunit;

namespace Hrot.Core.Tests;

/// <summary>
/// TKB-016 -- Unit tests for TkbName deserialization in ScenarioHeaderDto.
/// </summary>
public sealed class ScenarioHeaderDtoTests
{
    // ── Test 1 ────────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioHeaderDto_WithTkbName_Deserializes()
    {
        const string json = "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\",\"TkbName\":\"Sample_v1\"},\"Entities\":{}}";

        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope);
        Assert.NotNull(envelope!.Header);
        Assert.Equal("Sample_v1", envelope.Header!.TkbName);
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioHeaderDto_WithoutTkbName_IsNull()
    {
        const string json = "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\"},\"Entities\":{}}";

        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope);
        Assert.NotNull(envelope!.Header);
        Assert.Null(envelope.Header!.TkbName);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    [Fact]
    public void ScenarioHeaderDto_TkbNameNull_InJson_IsNull()
    {
        const string json = "{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\",\"TkbName\":null},\"Entities\":{}}";

        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope);
        Assert.NotNull(envelope!.Header);
        Assert.Null(envelope.Header!.TkbName);
    }
}

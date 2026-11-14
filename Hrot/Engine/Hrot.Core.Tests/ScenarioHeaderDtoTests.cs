using System.Text.Json;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Xunit;

namespace Hrot.Core.Tests;

/// <summary>
/// TKB-016 -- ⛔⛔ <c>R-4018</c>: the scenario header carries no TKB name. An OLDER file that still has one must load
/// unchanged — the field is simply ignored. (Was: <c>TkbName</c> deserializes into the header.)
/// </summary>
public sealed class ScenarioHeaderDtoTests
{
    [Fact]
    public void TheHeaderDto_HasNoTkbName()
    {
        Assert.Null(typeof(ScenarioHeaderDto).GetProperty("TkbName"));
    }

    [Theory]
    [InlineData("{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\",\"TkbName\":\"Sample_v1\",\"TerrainName\":\"basic-desert\"},\"Entities\":{}}")]
    [InlineData("{\"Header\":{\"SubsystemType\":\"Hrot.SimHost\",\"TkbName\":null,\"TerrainName\":\"basic-desert\"},\"Entities\":{}}")]
    public void ALegacyFileNamingATkb_StillDeserializes_AndKeepsItsTerrain(string json)
    {
        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(envelope);
        Assert.NotNull(envelope!.Header);
        Assert.Equal("Hrot.SimHost", envelope.Header!.SubsystemType);
        Assert.Equal("basic-desert", envelope.Header.TerrainName);
    }
}

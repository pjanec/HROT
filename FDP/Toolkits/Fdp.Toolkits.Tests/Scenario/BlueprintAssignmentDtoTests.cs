using System;
using System.Text.Json;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// BSA-201 / CE-3044: Tests for BlueprintAssignmentDto JSON round-trip. The persisted param form is a JSON
    /// object keyed by parameter NAME (R-191) — never bytes.
    /// </summary>
    public sealed class BlueprintAssignmentDtoTests
    {
        [Fact]
        public void Dto_RoundTrip_WithNullParams_OmitsParamsKey()
        {
            var dto = new BlueprintAssignmentDto
            {
                AssetId = Guid.NewGuid(),
                Params  = null,
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            };
            var json = JsonSerializer.Serialize(dto, options);
            Assert.DoesNotContain("\"Params\"", json);

            var deserialized = JsonSerializer.Deserialize<BlueprintAssignmentDto>(json, options);
            Assert.NotNull(deserialized);
            Assert.Equal(dto.AssetId, deserialized!.AssetId);
            Assert.Null(deserialized.Params);
        }

        [Fact]
        public void Dto_RoundTrip_WithParams_IsAJsonObjectByName()
        {
            var dto = new BlueprintAssignmentDto
            {
                AssetId = Guid.NewGuid(),
                Params  = new System.Text.Json.Nodes.JsonObject { ["Speed"] = 12.5, ["Name"] = "alpha" },
            };

            var json = JsonSerializer.Serialize(dto);
            Assert.Contains("\"Params\":{\"Speed\":12.5,\"Name\":\"alpha\"}", json);   // readable, by name — not base64

            var deserialized = JsonSerializer.Deserialize<BlueprintAssignmentDto>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(dto.AssetId, deserialized!.AssetId);
            Assert.Equal(12.5, (double)deserialized.Params!["Speed"]!);
            Assert.Equal("alpha", (string)deserialized.Params["Name"]!);
        }
    }
}

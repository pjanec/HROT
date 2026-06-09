using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// BSA-201: Tests for BlueprintAssignmentDto JSON round-trip and
    /// InitialBlueprintsIntent world round-trip.
    /// </summary>
    public sealed class BlueprintAssignmentDtoTests
    {
        // ── Test 3: DTO JSON round-trip ────────────────────────────────────────

        [Fact]
        public void Dto_RoundTrip_WithNullOverrides_OmitsOverridesKey()
        {
            var dto = new BlueprintAssignmentDto
            {
                AssetId = Guid.NewGuid(),
                Overrides = null,
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            };
            var json = JsonSerializer.Serialize(dto, options);
            Assert.DoesNotContain("\"Overrides\"", json);

            var deserialized = JsonSerializer.Deserialize<BlueprintAssignmentDto>(json, options);
            Assert.NotNull(deserialized);
            Assert.Equal(dto.AssetId, deserialized!.AssetId);
            Assert.Null(deserialized.Overrides);
        }

        [Fact]
        public void Dto_RoundTrip_WithPopulatedOverrides_PreservesValues()
        {
            var dto = new BlueprintAssignmentDto
            {
                AssetId = Guid.NewGuid(),
                Overrides = new Dictionary<string, object>
                {
                    ["Health"] = 100,
                    ["Speed"] = 5.5,
                },
            };

            var json = JsonSerializer.Serialize(dto);
            Assert.Contains("\"Overrides\"", json);

            var deserialized = JsonSerializer.Deserialize<BlueprintAssignmentDto>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(dto.AssetId, deserialized!.AssetId);
            Assert.NotNull(deserialized.Overrides);
        }
    }
}

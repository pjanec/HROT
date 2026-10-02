using System;
using System.Text.Json;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// BSA-201 / MX-030: Tests for BlueprintAssignmentDto JSON round-trip. The persisted param form is the
    /// RESOLVER-SHAPE bytes (Params) plus their layout hash — not the retired Overrides name→value dict
    /// (EXPLAINER §"two supply shapes, one concept"; ruling 9).
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
            Assert.DoesNotContain("\"ParamsStructureHash\"", json);

            var deserialized = JsonSerializer.Deserialize<BlueprintAssignmentDto>(json, options);
            Assert.NotNull(deserialized);
            Assert.Equal(dto.AssetId, deserialized!.AssetId);
            Assert.Null(deserialized.Params);
            Assert.Null(deserialized.ParamsStructureHash);
        }

        [Fact]
        public void Dto_RoundTrip_WithParams_PreservesBytesAndHash()
        {
            var bytes = new byte[] { 42, 0, 0, 0, 7, 0, 0, 0 };
            var dto = new BlueprintAssignmentDto
            {
                AssetId             = Guid.NewGuid(),
                Params              = bytes,
                ParamsStructureHash = 0xABCDEF01UL,
            };

            var json = JsonSerializer.Serialize(dto);
            Assert.Contains("\"Params\"", json);   // base64-encoded byte[]

            var deserialized = JsonSerializer.Deserialize<BlueprintAssignmentDto>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(dto.AssetId, deserialized!.AssetId);
            Assert.Equal(bytes, deserialized.Params);
            Assert.Equal(0xABCDEF01UL, deserialized.ParamsStructureHash);
        }
    }
}

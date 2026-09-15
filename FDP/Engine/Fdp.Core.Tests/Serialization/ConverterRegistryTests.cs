using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Converters;
using Xunit;

namespace Fdp.Tests.Serialization
{
    // ── DD-P1-T01: Converter unit tests ──────────────────────────────────────

    /// <summary>
    /// Tests that the canonical converters in <see cref="Fdp.Core.Serialization.Converters"/>
    /// are accessible and produce correct output. Covers DD-P1-T01 success conditions.
    /// </summary>
    public sealed class ConverterTests
    {
        private static JsonSerializerOptions MakeOpts(params JsonConverter[] converters)
        {
            var opts = new JsonSerializerOptions { IncludeFields = true };
            foreach (var c in converters) opts.Converters.Add(c);
            return opts;
        }

        // ── FixedString64 ─────────────────────────────────────────────────────

        [Fact]
        public void FixedString64Converter_Serialize_ReturnsQuotedString()
        {
            var opts = MakeOpts(new FixedString64Converter());
            string json = JsonSerializer.Serialize(new FixedString64("hello"), opts);
            Assert.Equal("\"hello\"", json);
        }

        [Fact]
        public void FixedString64Converter_Deserialize_ReturnsValue()
        {
            var opts = MakeOpts(new FixedString64Converter());
            var result = JsonSerializer.Deserialize<FixedString64>("\"hello\"", opts);
            Assert.Equal("hello", result.ToString());
        }

        [Fact]
        public void FixedString64Converter_Roundtrip()
        {
            var opts = MakeOpts(new FixedString64Converter());
            var original = new FixedString64("HealthDepleted");
            string json  = JsonSerializer.Serialize(original, opts);
            var result   = JsonSerializer.Deserialize<FixedString64>(json, opts);
            Assert.Equal(original.ToString(), result.ToString());
        }

        // ── FixedString32 ─────────────────────────────────────────────────────

        [Fact]
        public void FixedString32Converter_Serialize_ReturnsQuotedString()
        {
            var opts = MakeOpts(new FixedString32Converter());
            string json = JsonSerializer.Serialize(new FixedString32("abc"), opts);
            Assert.Equal("\"abc\"", json);
        }

        // ── FixedString128 ────────────────────────────────────────────────────

        [Fact]
        public void FixedString128Converter_Serialize_ReturnsQuotedString()
        {
            var opts = MakeOpts(new FixedString128Converter());
            string json = JsonSerializer.Serialize(new FixedString128("hello"), opts);
            Assert.Equal("\"hello\"", json);
        }

        [Fact]
        public void FixedString128Converter_Deserialize_ReturnsValue()
        {
            var opts = MakeOpts(new FixedString128Converter());
            var result = JsonSerializer.Deserialize<FixedString128>("\"hello\"", opts);
            Assert.Equal("hello", result.ToString());
        }

        [Fact]
        public void FixedString128Converter_Roundtrip()
        {
            var opts = MakeOpts(new FixedString128Converter());
            var original = new FixedString128("HealthDepleted");
            string json  = JsonSerializer.Serialize(original, opts);
            var result   = JsonSerializer.Deserialize<FixedString128>(json, opts);
            Assert.Equal(original.ToString(), result.ToString());
        }

        // ── StrictStringEnumConverter ─────────────────────────────────────────

        private enum TestEnum { Alpha, Beta, Gamma }

        [Fact]
        public void StrictStringEnumConverter_Serialize_ReturnsStringName()
        {
            var opts = MakeOpts(new StrictStringEnumConverter());
            string json = JsonSerializer.Serialize(TestEnum.Beta, opts);
            // Should be "Beta", not "1"
            Assert.Equal("\"Beta\"", json);
        }

        [Fact]
        public void StrictStringEnumConverter_Deserialize_FromString()
        {
            var opts = MakeOpts(new StrictStringEnumConverter());
            var result = JsonSerializer.Deserialize<TestEnum>("\"Gamma\"", opts);
            Assert.Equal(TestEnum.Gamma, result);
        }

        [Fact]
        public void StrictStringEnumConverter_Deserialize_IntegerThrows()
        {
            var opts = MakeOpts(new StrictStringEnumConverter());
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TestEnum>("1", opts));
        }

        // ── Vector converters ─────────────────────────────────────────────────

        [Fact]
        public void Vector3ArrayConverter_Roundtrip()
        {
            var opts     = MakeOpts(new Vector3ArrayConverter());
            var original = new Vector3(1.5f, -2.25f, 3.125f);
            string json  = JsonSerializer.Serialize(original, opts);
            var result   = JsonSerializer.Deserialize<Vector3>(json, opts);
            Assert.Equal(original, result);
        }

        [Fact]
        public void Vector3ArrayConverter_SerializesAsSingleLine()
        {
            var opts = MakeOpts(new Vector3ArrayConverter());
            string json = JsonSerializer.Serialize(new Vector3(1f, 2f, 3f), opts);
            Assert.DoesNotContain("\n", json);
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        // ── Non-finite robustness (regression) ────────────────────────────────
        // A stray NaN/Infinity component used to render as bare `NaN`/`Infinity` inside WriteRawValue,
        // which is invalid JSON → the writer threw and crashed whole-scenario save. The converters now
        // clamp non-finite components to 0 so serialization always produces valid, re-readable JSON.

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void Vector3ArrayConverter_NonFiniteComponent_ProducesValidJson_DoesNotThrow(float bad)
        {
            var opts = MakeOpts(new Vector3ArrayConverter());

            string json = JsonSerializer.Serialize(new Vector3(bad, 2f, 3f), opts);

            Assert.DoesNotContain("NaN", json);
            Assert.DoesNotContain("Infinity", json);
            // Round-trips as valid JSON: the bad component became 0, the rest survived.
            var back = JsonSerializer.Deserialize<Vector3>(json, opts);
            Assert.Equal(new Vector3(0f, 2f, 3f), back);
        }

        [Fact]
        public void Vector4AndQuaternionConverters_NonFinite_DoNotThrow()
        {
            var v4opts = MakeOpts(new Vector4ArrayConverter());
            var qopts  = MakeOpts(new QuaternionArrayConverter());

            var v4 = JsonSerializer.Deserialize<Vector4>(
                JsonSerializer.Serialize(new Vector4(float.NaN, 1f, 2f, 3f), v4opts), v4opts);
            Assert.Equal(new Vector4(0f, 1f, 2f, 3f), v4);

            var q = JsonSerializer.Deserialize<Quaternion>(
                JsonSerializer.Serialize(new Quaternion(1f, float.PositiveInfinity, 2f, 3f), qopts), qopts);
            Assert.Equal(new Quaternion(1f, 0f, 2f, 3f), q);
        }
    }

    // ── DD-P1-T02: FdpJsonOptionsRegistry tests ───────────────────────────────

    /// <summary>
    /// Tests that <see cref="FdpJsonOptionsRegistry"/> exposes the correct singletons.
    /// Covers DD-P1-T02 success conditions.
    /// </summary>
    public sealed class FdpJsonOptionsRegistryTests
    {
        [Fact]
        public void DefaultRelaxed_IsNotNull()
        {
            Assert.NotNull(FdpJsonOptionsRegistry.DefaultRelaxed);
        }

        [Fact]
        public void Indented_IsNotNull()
        {
            Assert.NotNull(FdpJsonOptionsRegistry.Indented);
        }

        [Fact]
        public void DefaultRelaxed_IsFrozen_MutationThrows()
        {
            Assert.Throws<InvalidOperationException>(
                () => FdpJsonOptionsRegistry.DefaultRelaxed.WriteIndented = true);
        }

        [Fact]
        public void Indented_IsFrozen_MutationThrows()
        {
            Assert.Throws<InvalidOperationException>(
                () => FdpJsonOptionsRegistry.Indented.WriteIndented = false);
        }

        [Fact]
        public void DefaultRelaxed_FixedString64_Roundtrip()
        {
            var opts   = FdpJsonOptionsRegistry.DefaultRelaxed;
            var result = JsonSerializer.Deserialize<FixedString64>("\"hello\"", opts);
            Assert.Equal("hello", result!.ToString());
        }

        [Fact]
        public void DefaultRelaxed_FixedString128_Roundtrip()
        {
            var opts   = FdpJsonOptionsRegistry.DefaultRelaxed;
            var result = JsonSerializer.Deserialize<FixedString128>("\"hello\"", opts);
            Assert.Equal("hello", result!.ToString());
        }

        [Fact]
        public void DefaultRelaxed_IncludesFields()
        {
            // A struct with only public fields (no properties) must produce non-empty JSON.
            var opts = FdpJsonOptionsRegistry.DefaultRelaxed;
            string json = JsonSerializer.Serialize(new FieldOnlyStruct { X = 42 }, opts);
            Assert.Contains("\"X\"", json);
            Assert.Contains("42", json);
        }

        [Fact]
        public void Indented_HasWriteIndented()
        {
            Assert.True(FdpJsonOptionsRegistry.Indented.WriteIndented);
        }

        [Fact]
        public void DefaultRelaxed_DoesNotHaveWriteIndented()
        {
            Assert.False(FdpJsonOptionsRegistry.DefaultRelaxed.WriteIndented);
        }

        // ── Struct with only public fields ────────────────────────────────────
        private struct FieldOnlyStruct
        {
            public int X;
        }
    }
}

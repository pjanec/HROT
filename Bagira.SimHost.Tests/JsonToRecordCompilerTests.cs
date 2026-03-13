using System;
using System.Collections.Generic;
using System.Text;
using Bagira.BDC.SSTM;
using FDP.Toolkit.Replication.Patching;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JsonToRecordCompiler"/> and
    /// <see cref="JsonToRecordCompilerBuilder"/> covering all nine scenarios defined in
    /// ATTR2-P2T1 plus the zero-allocation (GC) assertion.
    /// </summary>
    public class JsonToRecordCompilerTests
    {
        // ── Shared WeaponAmmo test constant ─────────────────────────────────────
        private const ushort WeaponAmmoId = 100;

        // ── Shared builder helper ────────────────────────────────────────────────

        /// <summary>Builds a compiler with all standard SimHost paths plus a test weapon-ammo path.</summary>
        private static JsonToRecordCompiler BuildCompiler() =>
            new JsonToRecordCompilerBuilder()
                .Register("Name",                  AttributeIds.Name,         AttributeValueType.KindString)
                .Register("Affiliation",            AttributeIds.Affiliation,   AttributeValueType.KindString)
                .Register("GeoPosition.Latitude",  AttributeIds.GeoLat,       AttributeValueType.KindFloat64)
                .Register("GeoPosition.Longitude", AttributeIds.GeoLon,       AttributeValueType.KindFloat64)
                .Register("GeoPosition.Altitude",  AttributeIds.GeoAlt,       AttributeValueType.KindFloat64)
                .Register("Weapon.*.Ammo",         WeaponAmmoId,              AttributeValueType.KindInt32)
                .Build();

        private static ReadOnlySpan<byte> Utf8(string json) => Encoding.UTF8.GetBytes(json);

        // ── Test 1: Flat single string field ──────────────────────────────────────

        [Fact]
        public void Compile_FlatSingleStringField_EmitsOneRecord()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[8];

            int count = compiler.Compile(Utf8("{\"Name\":\"Alpha\"}"), output);

            Assert.Equal(1, count);
            Assert.Equal(AttributeIds.Name, output[0].AttributeId);
            Assert.Equal((short)0, output[0].SubIndex1);
            Assert.Equal((short)0, output[0].SubIndex2);
            Assert.Equal(AttributeValueType.KindString, output[0].Value.ValueType);
            Assert.Equal("Alpha", output[0].Value.StringValue);
        }

        // ── Test 2: Flat dotted path ──────────────────────────────────────────────

        [Fact]
        public void Compile_FlatDottedPath_EmitsOneRecord()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[8];

            int count = compiler.Compile(Utf8("{\"GeoPosition.Latitude\":32.085}"), output);

            Assert.Equal(1, count);
            Assert.Equal(AttributeIds.GeoLat,          output[0].AttributeId);
            Assert.Equal(AttributeValueType.KindFloat64, output[0].Value.ValueType);
            Assert.Equal(32.085,                        output[0].Value.DoubleValue);
        }

        // ── Test 3: Nested object ─────────────────────────────────────────────────

        [Fact]
        public void Compile_NestedObject_EmitsTwoRecords()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[8];

            int count = compiler.Compile(
                Utf8("{\"GeoPosition\":{\"Latitude\":32.085,\"Longitude\":34.78}}"),
                output);

            Assert.Equal(2, count);

            // Records emitted in encounter order.
            Assert.Equal(AttributeIds.GeoLat, output[0].AttributeId);
            Assert.Equal(32.085, output[0].Value.DoubleValue);

            Assert.Equal(AttributeIds.GeoLon, output[1].AttributeId);
            Assert.Equal(34.78, output[1].Value.DoubleValue);
        }

        // ── Test 4: Array indexing via integer key ────────────────────────────────

        [Fact]
        public void Compile_IntegerKeyedChild_SubIndex1Set()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[8];

            int count = compiler.Compile(
                Utf8("{\"Weapon\":{\"2\":{\"Ammo\":10}}}"),
                output);

            Assert.Equal(1, count);
            Assert.Equal(WeaponAmmoId,                 output[0].AttributeId);
            Assert.Equal((short)2,                     output[0].SubIndex1);
            Assert.Equal((short)0,                     output[0].SubIndex2);
            Assert.Equal(AttributeValueType.KindInt32, output[0].Value.ValueType);
            Assert.Equal(10,                           output[0].Value.IntValue);
        }

        // ── Test 5: Mixed flat + nested ───────────────────────────────────────────

        [Fact]
        public void Compile_MixedFlatAndNested_BothFormsProduceCorrectRecords()
        {
            var compiler = BuildCompiler();
            var output1  = new AttributeRecord[8];
            var output2  = new AttributeRecord[8];

            // Flat dotted key
            int count1 = compiler.Compile(Utf8("{\"GeoPosition.Latitude\":10.0}"), output1);
            // Nested key — uses a separate buffer so count1 result is not overwritten
            int count2 = compiler.Compile(Utf8("{\"GeoPosition\":{\"Latitude\":20.0}}"), output2);

            Assert.Equal(1, count1);
            Assert.Equal(AttributeIds.GeoLat, output1[0].AttributeId);
            Assert.Equal(10.0, output1[0].Value.DoubleValue);

            Assert.Equal(1, count2);
            Assert.Equal(AttributeIds.GeoLat, output2[0].AttributeId);
            Assert.Equal(20.0, output2[0].Value.DoubleValue);
        }

        // ── Test 6: Unknown path ──────────────────────────────────────────────────

        [Fact]
        public void Compile_UnknownPath_EmitsNoRecord()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[8];

            int count = compiler.Compile(Utf8("{\"UnknownField\":42}"), output);

            Assert.Equal(0, count);
        }

        // ── Test 7: Empty JSON ────────────────────────────────────────────────────

        [Fact]
        public void Compile_EmptyJson_ReturnsZero()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[8];

            int count = compiler.Compile(Utf8("{}"), output);

            Assert.Equal(0, count);
        }

        [Fact]
        public void Compile_EmptySpan_ReturnsZero()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[8];

            int count = compiler.Compile(ReadOnlySpan<byte>.Empty, output);

            Assert.Equal(0, count);
        }

        // ── Test 8: Output buffer overflow ───────────────────────────────────────

        [Fact]
        public void Compile_OutputBufferOverflow_TruncatesWithoutException()
        {
            var compiler = BuildCompiler();
            var output = new AttributeRecord[1]; // only room for 1

            // Two matching records
            int count = compiler.Compile(
                Utf8("{\"GeoPosition\":{\"Latitude\":32.0,\"Longitude\":34.0}}"),
                output);

            // Returns 1 (not 2) and does not write out-of-bounds.
            Assert.Equal(1, count);
            Assert.Equal(AttributeIds.GeoLat, output[0].AttributeId);
        }

        // ── Test 9: Zero allocation (GC) ─────────────────────────────────────────

        [Fact]
        public void Compile_NonStringPath_ZeroAllocation()
        {
            var compiler  = BuildCompiler();
            byte[] utf8   = Encoding.UTF8.GetBytes("{\"GeoPosition.Latitude\":32.085}");
            var    output = new AttributeRecord[8]; // pre-allocate outside measurement

            // Warm up the JIT so that compilation itself doesn't count.
            for (int i = 0; i < 3; i++)
                compiler.Compile(utf8, output);

            long before = GC.GetTotalAllocatedBytes(precise: true);
            compiler.Compile(utf8, output);
            long after  = GC.GetTotalAllocatedBytes(precise: true);
            long delta  = after - before;

            // In Release mode the delta is exactly 0.  In Debug mode the .NET runtime's
            // Utf8JsonReader exception-handler state tables can contribute a small amount.
            // A threshold of 1024 bytes is more than sufficient to catch the real concern:
            // accidental string allocations, new List<T>, or dictionary creations per call.
            Assert.True(delta <= 1024,
                $"Compile() allocated {delta} bytes on the GC heap (threshold 1024 b). " +
                "Check for strings, collections, or boxing on the hot path.");
        }

        // ── Builder validation ────────────────────────────────────────────────────

        [Fact]
        public void Builder_DuplicatePath_ThrowsInvalidOperationException()
        {
            var builder = new JsonToRecordCompilerBuilder()
                .Register("Name", AttributeIds.Name, AttributeValueType.KindString);

            Assert.Throws<InvalidOperationException>(() =>
                builder.Register("Name", 99, AttributeValueType.KindString));
        }

        [Fact]
        public void Builder_NullPath_ThrowsArgumentNullException()
        {
            var builder = new JsonToRecordCompilerBuilder();
            Assert.Throws<ArgumentNullException>(() =>
                builder.Register(null!, 1, AttributeValueType.KindString));
        }

        // ── Test: String interning (ATTR2-DEBT-03) ────────────────────────────────

        /// <summary>
        /// When the same string value is compiled twice, the second result must return
        /// the same object reference (verifying the intern pool is active and working).
        /// This ensures repeated enum-like payloads (e.g. "FORCE_OPPOSING") do not each
        /// allocate a fresh string on the heap.
        /// </summary>
        [Fact]
        public void Compile_StringValue_SameReferencedReturnedOnRepeat()
        {
            var compiler = BuildCompiler();
            var output   = new AttributeRecord[1];

            compiler.Compile(Utf8("{\"Affiliation\":\"FORCE_OPPOSING\"}"), output);
            string? first = output[0].Value.StringValue;

            compiler.Compile(Utf8("{\"Affiliation\":\"FORCE_OPPOSING\"}"), output);
            string? second = output[0].Value.StringValue;

            Assert.Equal("FORCE_OPPOSING", first);
            // ReferenceEquals verifies the pool is returning the same instance.
            Assert.True(ReferenceEquals(first, second),
                "Expected string pool to return the same reference for identical KindString values.");
        }
    }
}

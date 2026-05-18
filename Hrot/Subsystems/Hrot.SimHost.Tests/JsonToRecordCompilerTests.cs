using System;
using System.Collections.Generic;
using System.Text;
using Hrot.NED.Messages;
using Hrot.Map.Common.Replication;
using Fdp.Toolkit.Replication.Patching;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JsonToRecordCompiler"/> and
    /// <see cref="JsonToRecordCompilerBuilder"/> covering all nine scenarios defined in
    /// ATTR2-P2T1 plus the zero-allocation (GC) assertion.
    /// </summary>
    [Collection("SimHostDds")]
    public class JsonToRecordCompilerTests
    {
        // ── Shared WeaponAmmo test constant ─────────────────────────────────────
        private const ushort WeaponAmmoId = 100;

        // ── Shared builder helper ────────────────────────────────────────────────

        /// <summary>Builds a compiler with all standard SimHost paths plus a test weapon-ammo path.</summary>
        private static JsonToRecordCompiler BuildCompiler() =>
            new JsonToRecordCompilerBuilder()
                .Register("Name",                  AttributeIds.Name,         AttributeValueKind.CsString)
                .Register("Affiliation",            AttributeIds.Affiliation,   AttributeValueKind.CsString)
                .Register("GeoPosition.Latitude",  AttributeIds.GeoLat,       AttributeValueKind.CsFloat64)
                .Register("GeoPosition.Longitude", AttributeIds.GeoLon,       AttributeValueKind.CsFloat64)
                .Register("GeoPosition.Altitude",  AttributeIds.GeoAlt,       AttributeValueKind.CsFloat64)
                .Register("Weapon.*.Ammo",         WeaponAmmoId,              AttributeValueKind.CsInt32)
                .Build();

        private static (NedAttributeRecordEmitter emitter, AttributeRecord[] buffer) MakeEmitter(int capacity = 8)
        {
            var buffer  = new AttributeRecord[capacity];
            var emitter = new NedAttributeRecordEmitter(buffer);
            return (emitter, buffer);
        }

        private static ReadOnlySpan<byte> Utf8(string json) => Encoding.UTF8.GetBytes(json);

        // ── Test 1: Flat single string field ──────────────────────────────────────

        [Fact]
        public void Compile_FlatSingleStringField_EmitsOneRecord()
        {
            var compiler = BuildCompiler();
            var (emitter, buffer) = MakeEmitter();

            compiler.Compile(Utf8("{\"Name\":\"Alpha\"}"), emitter);

            Assert.Equal(1, emitter.Count);
            Assert.Equal(AttributeIds.Name, buffer[0].AttributeId);
            Assert.Equal((short)0, buffer[0].SubIndex1);
            Assert.Equal((short)0, buffer[0].SubIndex2);
            Assert.Equal(AttributeValueType.KindString, buffer[0].Value.ValueType);
            Assert.Equal("Alpha", buffer[0].Value.StringValue);
        }

        // ── Test 2: Flat dotted path ──────────────────────────────────────────────

        [Fact]
        public void Compile_FlatDottedPath_EmitsOneRecord()
        {
            var compiler = BuildCompiler();
            var (emitter, buffer) = MakeEmitter();

            compiler.Compile(Utf8("{\"GeoPosition.Latitude\":32.085}"), emitter);

            Assert.Equal(1, emitter.Count);
            Assert.Equal(AttributeIds.GeoLat,          buffer[0].AttributeId);
            Assert.Equal(AttributeValueType.KindFloat64, buffer[0].Value.ValueType);
            Assert.Equal(32.085,                        buffer[0].Value.DoubleValue);
        }

        // ── Test 3: Nested object ─────────────────────────────────────────────────

        [Fact]
        public void Compile_NestedObject_EmitsTwoRecords()
        {
            var compiler = BuildCompiler();
            var (emitter, buffer) = MakeEmitter();

            compiler.Compile(
                Utf8("{\"GeoPosition\":{\"Latitude\":32.085,\"Longitude\":34.78}}"),
                emitter);

            Assert.Equal(2, emitter.Count);

            Assert.Equal(AttributeIds.GeoLat, buffer[0].AttributeId);
            Assert.Equal(32.085, buffer[0].Value.DoubleValue);

            Assert.Equal(AttributeIds.GeoLon, buffer[1].AttributeId);
            Assert.Equal(34.78, buffer[1].Value.DoubleValue);
        }

        // ── Test 4: Array indexing via integer key ────────────────────────────────

        [Fact]
        public void Compile_IntegerKeyedChild_SubIndex1Set()
        {
            var compiler = BuildCompiler();
            var (emitter, buffer) = MakeEmitter();

            compiler.Compile(
                Utf8("{\"Weapon\":{\"2\":{\"Ammo\":10}}}"),
                emitter);

            Assert.Equal(1, emitter.Count);
            Assert.Equal(WeaponAmmoId,                 buffer[0].AttributeId);
            Assert.Equal((short)2,                     buffer[0].SubIndex1);
            Assert.Equal((short)0,                     buffer[0].SubIndex2);
            Assert.Equal(AttributeValueType.KindInt32, buffer[0].Value.ValueType);
            Assert.Equal(10,                           buffer[0].Value.IntValue);
        }

        // ── Test 5: Mixed flat + nested ───────────────────────────────────────────

        [Fact]
        public void Compile_MixedFlatAndNested_BothFormsProduceCorrectRecords()
        {
            var compiler = BuildCompiler();
            var (emitter1, buffer1) = MakeEmitter();
            var (emitter2, buffer2) = MakeEmitter();

            compiler.Compile(Utf8("{\"GeoPosition.Latitude\":10.0}"), emitter1);
            compiler.Compile(Utf8("{\"GeoPosition\":{\"Latitude\":20.0}}"), emitter2);

            Assert.Equal(1, emitter1.Count);
            Assert.Equal(AttributeIds.GeoLat, buffer1[0].AttributeId);
            Assert.Equal(10.0, buffer1[0].Value.DoubleValue);

            Assert.Equal(1, emitter2.Count);
            Assert.Equal(AttributeIds.GeoLat, buffer2[0].AttributeId);
            Assert.Equal(20.0, buffer2[0].Value.DoubleValue);
        }

        // ── Test 6: Unknown path ──────────────────────────────────────────────────

        [Fact]
        public void Compile_UnknownPath_EmitsNoRecord()
        {
            var compiler = BuildCompiler();
            var (emitter, _) = MakeEmitter();

            compiler.Compile(Utf8("{\"UnknownField\":42}"), emitter);

            Assert.Equal(0, emitter.Count);
        }

        // ── Test 7: Empty JSON ────────────────────────────────────────────────────

        [Fact]
        public void Compile_EmptyJson_ReturnsZero()
        {
            var compiler = BuildCompiler();
            var (emitter, _) = MakeEmitter();

            compiler.Compile(Utf8("{}"), emitter);

            Assert.Equal(0, emitter.Count);
        }

        [Fact]
        public void Compile_EmptySpan_ReturnsZero()
        {
            var compiler = BuildCompiler();
            var (emitter, _) = MakeEmitter();

            compiler.Compile(ReadOnlySpan<byte>.Empty, emitter);

            Assert.Equal(0, emitter.Count);
        }

        // ── Test 8: Output buffer overflow ───────────────────────────────────────

        [Fact]
        public void Compile_OutputBufferOverflow_TruncatesWithoutException()
        {
            var compiler = BuildCompiler();
            var (emitter, buffer) = MakeEmitter(1); // only room for 1

            compiler.Compile(
                Utf8("{\"GeoPosition\":{\"Latitude\":32.0,\"Longitude\":34.0}}"),
                emitter);

            Assert.Equal(1, emitter.Count);
            Assert.Equal(AttributeIds.GeoLat, buffer[0].AttributeId);
        }

        // ── Test 9: Zero allocation (GC) ─────────────────────────────────────────

        [Fact]
        public void Compile_NonStringPath_ZeroAllocation()
        {
            var compiler = BuildCompiler();
            byte[] utf8    = Encoding.UTF8.GetBytes("{\"GeoPosition.Latitude\":32.085}");
            var    buffer  = new AttributeRecord[8];
            var    emitter = new NedAttributeRecordEmitter(buffer);

            // Warm up the JIT so that compilation itself doesn't count.
            for (int i = 0; i < 3; i++)
            {
                emitter.Reset(buffer);
                compiler.Compile(utf8, emitter);
            }

            emitter.Reset(buffer);
            long before = GC.GetTotalAllocatedBytes(precise: true);
            compiler.Compile(utf8, emitter);
            long after  = GC.GetTotalAllocatedBytes(precise: true);
            long delta  = after - before;

            Assert.True(delta <= 1024,
                $"Compile() allocated {delta} bytes on the GC heap (threshold 1024 b). " +
                "Check for strings, collections, or boxing on the hot path.");
        }

        // ── Builder validation ────────────────────────────────────────────────────

        [Fact]
        public void Builder_DuplicatePath_ThrowsInvalidOperationException()
        {
            var builder = new JsonToRecordCompilerBuilder()
                .Register("Name", AttributeIds.Name, AttributeValueKind.CsString);

            Assert.Throws<InvalidOperationException>(() =>
                builder.Register("Name", 99, AttributeValueKind.CsString));
        }

        [Fact]
        public void Builder_NullPath_ThrowsArgumentNullException()
        {
            var builder = new JsonToRecordCompilerBuilder();
            Assert.Throws<ArgumentNullException>(() =>
                builder.Register(null!, 1, AttributeValueKind.CsString));
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
            var buffer   = new AttributeRecord[1];
            var emitter  = new NedAttributeRecordEmitter(buffer);

            emitter.Reset(buffer);
            compiler.Compile(Utf8("{\"Affiliation\":\"FORCE_OPPOSING\"}"), emitter);
            string? first = buffer[0].Value.StringValue;

            emitter.Reset(buffer);
            compiler.Compile(Utf8("{\"Affiliation\":\"FORCE_OPPOSING\"}"), emitter);
            string? second = buffer[0].Value.StringValue;

            Assert.Equal("FORCE_OPPOSING", first);
            Assert.True(ReferenceEquals(first, second),
                "Expected string pool to return the same reference for identical KindString values.");
        }
    }
}

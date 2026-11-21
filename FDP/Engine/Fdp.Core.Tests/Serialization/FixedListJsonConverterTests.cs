using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Xunit;

namespace Fdp.Tests.Serialization
{
    // ── Fixture wrapper types (the canonical Q#21-A1 shape) ──────────────────

    [InlineArray(4)]
    public struct IntBuf4 { private int _e0; }
    public struct IntList4 { public int Count; public IntBuf4 Items; }

    [InlineArray(3)]
    public struct Vec3Buf3 { private Vector3 _e0; }
    public struct Vec3List3 { public int Count; public Vec3Buf3 Items; }

    public struct Waypoint { public Vector3 Pos; public float Radius; }

    [InlineArray(2)]
    public struct WaypointBuf2 { private Waypoint _e0; }
    public struct WaypointList2 { public int Count; public WaypointBuf2 Items; }

    [InlineArray(3)]
    public struct EntityBuf3 { private Entity _e0; }
    public struct EntityList3 { public int Count; public EntityBuf3 Items; }

    /// <summary>A DTO hosting a list field beside scalars — the ParseParams shape.</summary>
    public struct PatrolParams
    {
        public float Speed;
        public IntList4 Stops;
    }

    /// <summary>
    /// FC-3b (Q#21-C3/C1) — the fixed-list JSON converter through the CANONICAL platform
    /// options (<see cref="FdpJsonOptionsRegistry.DefaultRelaxed"/>, where it is registered):
    /// plain-array authoring, Count = length clamped to [0, N], G6-zeroed tail, used-window
    /// writes, and element support INHERITED from the enclosing options (vectors' compact
    /// form, custom unmanaged structs via IncludeFields, Entity structurally).
    /// </summary>
    public sealed class FixedListJsonConverterTests
    {
        private static readonly JsonSerializerOptions Opts = FdpJsonOptionsRegistry.DefaultRelaxed;

        private static Span<TElem> ElementSpan<TBuf, TElem>(ref TBuf buf, int capacity)
            where TBuf : struct where TElem : struct
            => MemoryMarshal.CreateSpan(ref Unsafe.As<TBuf, TElem>(ref buf), capacity);

        // ---- read: plain array → wrapper ------------------------------------

        [Fact]
        public void Read_PlainArray_SetsCountAndPrefix_TailStaysZero()
        {
            var list = JsonSerializer.Deserialize<IntList4>("[3, 7]", Opts);

            Assert.Equal(2, list.Count);
            var span = ElementSpan<IntBuf4, int>(ref list.Items, 4);
            Assert.Equal(3, span[0]);
            Assert.Equal(7, span[1]);
            Assert.Equal(0, span[2]);              // G6: unused tail is default bytes
            Assert.Equal(0, span[3]);
        }

        [Fact]
        public void Read_BeyondCapacity_ClampsToN_DropsExtras()
        {
            var list = JsonSerializer.Deserialize<IntList4>("[1, 2, 3, 4, 5, 6]", Opts);

            Assert.Equal(4, list.Count);           // clamped to capacity (BP1504's authoring twin)
            var span = ElementSpan<IntBuf4, int>(ref list.Items, 4);
            Assert.Equal(new[] { 1, 2, 3, 4 }, span.ToArray());
        }

        [Fact]
        public void Read_EmptyArray_And_Null_BothGiveEmptyList()
        {
            Assert.Equal(0, JsonSerializer.Deserialize<IntList4>("[]", Opts).Count);
            Assert.Equal(0, JsonSerializer.Deserialize<IntList4>("null", Opts).Count);
        }

        [Fact]
        public void Read_NonArrayToken_Throws()
            => Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<IntList4>("{\"Count\":2}", Opts));

        // ---- element types inherited from the enclosing options -------------

        [Fact]
        public void Read_Vector3Elements_UseCompactArrayForm()
        {
            // Vector3ArrayConverter's [x,y,z] form — inherited, no list-specific code.
            var list = JsonSerializer.Deserialize<Vec3List3>("[[1,2,3],[4,5,6]]", Opts);

            Assert.Equal(2, list.Count);
            var span = ElementSpan<Vec3Buf3, Vector3>(ref list.Items, 3);
            Assert.Equal(new Vector3(1, 2, 3), span[0]);
            Assert.Equal(new Vector3(4, 5, 6), span[1]);
            Assert.Equal(default, span[2]);
        }

        [Fact]
        public void Read_CustomStructElements_FieldWise()
        {
            var json = "[{\"Pos\":[1,2,3],\"Radius\":5.5}]";
            var list = JsonSerializer.Deserialize<WaypointList2>(json, Opts);

            Assert.Equal(1, list.Count);
            var span = ElementSpan<WaypointBuf2, Waypoint>(ref list.Items, 2);
            Assert.Equal(new Vector3(1, 2, 3), span[0].Pos);
            Assert.Equal(5.5f, span[0].Radius);
        }

        [Fact]
        public void EntityElements_NullRoundTrips()
        {
            // C-e: Entity lists are structurally supported; the meaningful authored value is
            // Entity.Null (handles are runtime-assigned) — an empty list round-trips clean.
            var empty = JsonSerializer.Deserialize<EntityList3>("[]", Opts);
            Assert.Equal(0, empty.Count);
            Assert.Equal("[]", JsonSerializer.Serialize(empty, Opts));
        }

        // ---- write: used window only ----------------------------------------

        [Fact]
        public void Write_EmitsUsedWindowOnly_NoCountProperty()
        {
            var list = JsonSerializer.Deserialize<IntList4>("[3, 7]", Opts);
            var json = JsonSerializer.Serialize(list, Opts);

            Assert.Equal("[3,7]", json);           // plain array, no Count, no tail
        }

        [Fact]
        public void Write_CorruptNegativeCount_WritesEmptyArray()
        {
            var list = new IntList4 { Count = -5 };
            Assert.Equal("[]", JsonSerializer.Serialize(list, Opts));

            var over = new IntList4 { Count = 99 };            // F2: overflow clamps to capacity
            Assert.Equal("[0,0,0,0]", JsonSerializer.Serialize(over, Opts));
        }

        // ---- host-DTO round-trip (the ParseParams shape) ---------------------

        [Fact]
        public void HostDto_RoundTrips_ListBesideScalars()
        {
            var json = "{\"Speed\":2.5,\"Stops\":[10,20,30]}";
            var dto = JsonSerializer.Deserialize<PatrolParams>(json, Opts);

            Assert.Equal(2.5f, dto.Speed);
            Assert.Equal(3, dto.Stops.Count);

            var back = JsonSerializer.Serialize(dto, Opts);
            Assert.Contains("\"Stops\":[10,20,30]", back);
            Assert.DoesNotContain("Count", back);
        }

        [Fact]
        public void ByteImage_MatchesDirectSpanWrites_CanonicalForSnapshots()
        {
            // The converter-built wrapper must be byte-identical to a runtime-built one
            // (same Count, same prefix, zero tail) — the G6 canonical-image guarantee.
            var fromJson = JsonSerializer.Deserialize<IntList4>("[3, 7]", Opts);

            var direct = new IntList4 { Count = 2 };
            var span = ElementSpan<IntBuf4, int>(ref direct.Items, 4);
            span[0] = 3; span[1] = 7;

            Assert.True(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref fromJson, 1))
                .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref direct, 1))));
        }
    }
}

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests.Serialization
{
    // ── Fixture wrappers (reuse the converter tests' shapes + order/composite variants) ──

    [StructLayout(LayoutKind.Sequential)]
    public struct FmtFlippedList { public IntBuf4 Items; public int Count; }   // buffer FIRST

    /// <summary>
    /// FC-3c — the CANONICAL summary formatter (<see cref="FixedListFormatter"/>), shared by
    /// the Blueprints debugger watch and the StructEdit view provider: structural recognition
    /// (generated and hand-authored wrappers alike), real field offsets (Count need not be
    /// first), F2-clamped window, per-type element rendering with a "…" composite fallback.
    /// </summary>
    public sealed class FixedListFormatterTests
    {
        private static byte[] Bytes<T>(in T value) where T : struct
        {
            var arr = new byte[Marshal.SizeOf<T>()];
            MemoryMarshal.Write(arr, in Unsafe.AsRef(in value));
            return arr;
        }

        [Fact]
        public void IntWrapper_RendersCanonicalSummary()
        {
            var list = new IntList4 { Count = 2 };
            var span = MemoryMarshal.CreateSpan(ref Unsafe.As<IntBuf4, int>(ref list.Items), 4);
            span[0] = 5; span[1] = 7;

            Assert.True(FixedListFormatter.TryFormat(Bytes(list), typeof(IntList4), out var s));
            Assert.Equal("List<Int32>[4] Count=2 {5, 7}", s);
        }

        [Fact]
        public void BufferFirstFieldOrder_CountReadAtItsRealOffset()
        {
            var list = new FmtFlippedList { Count = 1 };
            var span = MemoryMarshal.CreateSpan(ref Unsafe.As<IntBuf4, int>(ref list.Items), 4);
            span[0] = 42;

            Assert.True(FixedListFormatter.TryFormat(Bytes(list), typeof(FmtFlippedList), out var s));
            Assert.Equal("List<Int32>[4] Count=1 {42}", s);
        }

        [Fact]
        public void Vector3Elements_RenderAsValues_CompositeStructsAsEllipsis()
        {
            var vecs = new Vec3List3 { Count = 1 };
            var vspan = MemoryMarshal.CreateSpan(ref Unsafe.As<Vec3Buf3, Vector3>(ref vecs.Items), 3);
            vspan[0] = new Vector3(1, 2, 3);
            Assert.True(FixedListFormatter.TryFormat(Bytes(vecs), typeof(Vec3List3), out var v));
            Assert.StartsWith("List<Vector3>[3] Count=1 {<1", v);

            var wps = new WaypointList2 { Count = 1 };
            Assert.True(FixedListFormatter.TryFormat(Bytes(wps), typeof(WaypointList2), out var w));
            Assert.Equal("List<Waypoint>[2] Count=1 {…}", w);
        }

        [Fact]
        public void GarbageCount_ClampsShownWindow_ShowsRawCount()
        {
            var over = new IntList4 { Count = 99 };
            Assert.True(FixedListFormatter.TryFormat(Bytes(over), typeof(IntList4), out var o));
            Assert.Equal("List<Int32>[4] Count=99 {0, 0, 0, 0}", o);

            var neg = new IntList4 { Count = -5 };
            Assert.True(FixedListFormatter.TryFormat(Bytes(neg), typeof(IntList4), out var n));
            Assert.Equal("List<Int32>[4] Count=-5 {}", n);
        }

        [Fact]
        public void NonWrapperTypes_Refused()
        {
            Assert.False(FixedListFormatter.TryFormat(new byte[16], typeof(Guid), out _));
            Assert.False(FixedListFormatter.TryFormat(new byte[8], typeof(long), out _));
            Assert.False(FixedListFormatter.TryFormat(new byte[12], typeof(Vector3), out _));
        }
    }
}

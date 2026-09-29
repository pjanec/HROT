using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-427</c> — what <c>BehaviorParams.FromBlockResolver</c> DOES at run time: the typed
    /// block resolver sees the authored DTO (a CLASS, deserialized) and the WHOLE block by <c>ref</c>, and
    /// writes it in place over the defaults the bake already laid down. 📄 <c>Q76</c> §12.19.
    /// </summary>
    public unsafe class BehaviorParamsBlockResolverTests
    {
        public sealed class GeoIntent { public double Lat { get; set; } public double Lon { get; set; } }

        [StructLayout(LayoutKind.Explicit)]
        private struct GeoBlock
        {
            [FieldOffset(0)]  public float X;
            [FieldOffset(4)]  public float Y;
            [FieldOffset(8)]  public int   Ticks;      // State — the bake's value must survive
            [FieldOffset(12)] public int   Seen;       // State — the resolver may write it
        }

        private static void Parse(ParseParamsDelegate p, string json, ref GeoBlock block, int capacity)
        {
            fixed (GeoBlock* b = &block)
                p(json, (byte*)b, capacity, null!, default, null);
        }

        /// <summary>
        /// ⭐⭐ The resolver converts the authored DTO into the Input region AND writes State, in place.
        /// ⭐ A field it does not touch keeps the baked default — the adapter neither clears nor bakes.
        ///
        /// <para>⚠ Inverse-edit red-proof: make the adapter pass a COPY of the block
        /// (<c>var tmp = Unsafe.Read&lt;TBlock&gt;(memory); resolve(in authored, ref tmp, …)</c>) and
        /// X/Y/Seen stay at their pre-parse values.</para>
        /// </summary>
        [Fact]
        public void TheResolverWritesTheWholeBlockInPlace_OverTheBakedDefaults()
        {
            var parse = BehaviorParams.FromBlockResolver<GeoIntent, GeoBlock>(
                (in GeoIntent a, ref GeoBlock b, EntityRepository w, Entity s, IHostVariableAccess? h) =>
                {
                    b.X = (float)a.Lon * 10f;
                    b.Y = (float)a.Lat * 10f;
                    b.Seen = 1;
                });

            var block = new GeoBlock { Ticks = 7 };                     // as the bake left it
            Parse(parse, "{\"Lat\":2,\"Lon\":3}", ref block, sizeof(GeoBlock));

            Assert.Equal(30f, block.X);
            Assert.Equal(20f, block.Y);
            Assert.Equal(1, block.Seen);
            Assert.Equal(7, block.Ticks);
        }

        /// <summary>⭐ Empty JSON hands the resolver <c>default</c> — for a class DTO, <c>null</c>.</summary>
        [Fact]
        public void EmptyJson_HandsTheResolverADefaultDto()
        {
            bool sawNull = false;
            var parse = BehaviorParams.FromBlockResolver<GeoIntent, GeoBlock>(
                (in GeoIntent a, ref GeoBlock b, EntityRepository w, Entity s, IHostVariableAccess? h) => sawNull = a is null);

            var block = default(GeoBlock);
            Parse(parse, "", ref block, sizeof(GeoBlock));
            Assert.True(sawNull);
        }

        /// <summary>⛔ A buffer narrower than the block is a stale layout — refused, never overrun.</summary>
        [Fact]
        public void ANarrowerBuffer_IsRefused()
        {
            bool ran = false;
            var parse = BehaviorParams.FromBlockResolver<GeoIntent, GeoBlock>(
                (in GeoIntent a, ref GeoBlock b, EntityRepository w, Entity s, IHostVariableAccess? h) => ran = true);

            var block = default(GeoBlock);
            Assert.Throws<InvalidOperationException>(() => Parse(parse, "{}", ref block, sizeof(GeoBlock) - 1));
            Assert.False(ran);
        }
    }
}

// SC-GZ030: DebugPrimitive.SubElementId storage and PickToken propagation.
using System.Runtime.InteropServices;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;
using CoreEntity = Fdp.Core.Entity;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    public class DebugPrimitiveSubElementTests
    {
        // SC-GZ030-1: DebugPrimitive still fits in 64 bytes after adding SubElementId.
        [Fact]
        public void SC_GZ030_1_StructSizeIs64()
        {
            Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
        }

        // SC-GZ030-2: DrawEntityLocalInteractive with subElementId=3 emits Token.SubElementId==3.
        [Fact]
        public void SC_GZ030_2_DrawEntityLocalInteractive_SetsSubElementId()
        {
            var buf    = new DebugPrimitiveBuffer(16);
            var entity = new CoreEntity(1, 1);
            buf.DrawEntityLocalInteractive(
                entity, Vector3.Zero, Vector3.UnitX,
                Rgba32.Red, subElementId: 3);

            var frame = buf.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(3u, frame[0].GetPickToken().SubElementId);
        }

        // SC-GZ030-3: Two calls with the same entity but different subElementId values
        // produce distinguishable tokens.
        [Fact]
        public void SC_GZ030_3_TwoCalls_DifferentSubElementIds_AreDistinguishable()
        {
            var buf    = new DebugPrimitiveBuffer(16);
            var entity = new CoreEntity(2, 1);
            buf.DrawEntityLocalInteractive(entity, Vector3.Zero, Vector3.UnitX, Rgba32.Red, subElementId: 1);
            buf.DrawEntityLocalInteractive(entity, Vector3.Zero, Vector3.UnitY, Rgba32.Red, subElementId: 2);

            var frame = buf.GetFrame();
            Assert.Equal(2, frame.Length);
            Assert.Equal(1u, frame[0].GetPickToken().SubElementId);
            Assert.Equal(2u, frame[1].GetPickToken().SubElementId);
        }

        // SC-GZ030-4: A zero-value DebugPrimitive has Token.SubElementId == 0.
        [Fact]
        public void SC_GZ030_4_DefaultPrimitive_HasSubElementIdZero()
        {
            var p = default(DebugPrimitive);
            Assert.Equal(0u, p.GetPickToken().SubElementId);
        }

        // SC-GZ030-5 (regression): Non-interactive DrawEntityLocal still emits SubElementId == 0.
        [Fact]
        public void SC_GZ030_5_DrawEntityLocal_SubElementIdIsZero()
        {
            var buf    = new DebugPrimitiveBuffer(16);
            var entity = new CoreEntity(3, 1);
            buf.DrawEntityLocal(entity, Vector3.Zero, Vector3.UnitX, Rgba32.Green);

            var frame = buf.GetFrame();
            Assert.Equal(1, frame.Length);
            Assert.Equal(0u, frame[0].GetPickToken().SubElementId);
        }
    }
}

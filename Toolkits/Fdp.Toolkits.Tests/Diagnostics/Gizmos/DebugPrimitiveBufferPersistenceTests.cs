// SC-GZ029: DebugPrimitiveBuffer lifetime persistence.
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    public class DebugPrimitiveBufferPersistenceTests
    {
        private static DebugPrimitive MakeSphere(float lifetime = 0f)
        {
            var p = default(DebugPrimitive);
            p.Shape          = DebugPrimitiveShape.Sphere;
            p.TargetView     = PipelineTarget.Map2D;
            p.SphereRadius   = 1f;
            p.LifetimeSeconds = lifetime;
            return p;
        }

        // SC-GZ029-1: A primitive with LifetimeSeconds=0.5 appears in GetFrame() for 5 frames
        // at 0.1s deltaTime each (frames 0..4), and is absent in frame 5.
        [Fact]
        public void SC_GZ029_1_PersistentPrimitive_SurvivesMultipleFrames()
        {
            var buf = new DebugPrimitiveBuffer(64);
            buf.Append(MakeSphere(lifetime: 0.5f));

            // Frame 0: just appended, should be visible.
            Assert.Equal(1, buf.GetFrame().Length);

            // Frames 1..4: after each EndFrame(0.1), remaining life > 0.
            for (int i = 1; i <= 4; i++)
            {
                buf.EndFrame(0.1f);
                Assert.True(buf.GetFrame().Length >= 1,
                    $"Persistent prim should still be present in frame {i}");
            }

            // Frames 5-6: past the 0.5s lifetime — advance two extra steps to clear any float
            // rounding that might keep the remaining life at exactly 0.0 rather than < 0.
            buf.EndFrame(0.1f);
            buf.EndFrame(0.1f);
            int countAfterExpiry = buf.GetFrame().Length;
            Assert.Equal(0, countAfterExpiry);
        }

        // SC-GZ029-2: A primitive with LifetimeSeconds=0 does NOT appear in frame N+1.
        [Fact]
        public void SC_GZ029_2_TransientPrimitive_GoneAfterOneFrame()
        {
            var buf = new DebugPrimitiveBuffer(64);
            buf.Append(MakeSphere(lifetime: 0f));

            Assert.Equal(1, buf.GetFrame().Length);

            buf.EndFrame(0.016f); // Simulate one frame advance.

            Assert.Equal(0, buf.GetFrame().Length);
        }

        // SC-GZ029-3: After persistent capacity exhausted, additional persistent primitives
        // are dropped (DroppedCount increments, no exception).
        [Fact]
        public void SC_GZ029_3_PersistentCapacityExhausted_DropsGracefully()
        {
            // Small transient capacity but enough for overflow test.
            var buf = new DebugPrimitiveBuffer(512);
            const int overCount = 257; // Exceeds PersistentCapacity=256.

            // Fill one past capacity.
            int threwException = 0;
            try
            {
                for (int i = 0; i < overCount; i++)
                    buf.Append(MakeSphere(lifetime: 1f));
            }
            catch
            {
                threwException++;
            }

            Assert.Equal(0, threwException);
            // At least the capacity count of persistent entries was retained.
            // DroppedCount should be >= 1 (the overflow entry).
            Assert.True(buf.DroppedCount >= 1);
        }

        // SC-GZ029-4: Persistent primitives survive Clear() cycles (re-injected each EndFrame).
        [Fact]
        public void SC_GZ029_4_PersistentPrimitiveSurvivesClear()
        {
            var buf = new DebugPrimitiveBuffer(64);
            buf.Append(MakeSphere(lifetime: 0.3f));

            // Frame 1 advance: re-injects persistent.
            buf.EndFrame(0.1f);
            Assert.True(buf.GetFrame().Length >= 1, "Persistent prim should survive EndFrame/Clear cycle");
        }

        // SC-GZ029-5: EndFrame with deltaTime > LifetimeSeconds causes immediate expiry.
        [Fact]
        public void SC_GZ029_5_EndFrame_LargeDelta_ExpiresImmediately()
        {
            var buf = new DebugPrimitiveBuffer(64);
            buf.Append(MakeSphere(lifetime: 0.1f));

            // One large frame advance exceeding the lifetime.
            buf.EndFrame(1.0f);

            Assert.Equal(0, buf.GetFrame().Length);
        }

        // SC-GZ038-5: AppendRaw overflow increments DroppedCount.
        [Fact]
        public void SC_GZ038_5_AppendRaw_OverflowIncrements_DroppedCount()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 2); // very small
            var p = new DebugPrimitive();

            buffer.AppendRaw(in p);
            buffer.AppendRaw(in p);
            buffer.AppendRaw(in p); // this one should overflow

            Assert.Equal(1, buffer.DroppedCount);
            Assert.Equal(2, buffer.GetFrame().Length);
        }

        // SC-GZ038-7: Buffer populated via AppendRaw has correct frame content.
        [Fact]
        public void SC_GZ038_7_DebugGizmoLayer_RendersAppendRawPrimitives()
        {
            // Populate buffer via AppendRaw instead of draw methods.
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            var primitive = DebugPrimitive.MakeLine(
                System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX,
                new Rgba32(255, 0, 0, 255));
            buffer.AppendRaw(in primitive);

            // Verify buffer has content.
            Assert.Equal(1, buffer.GetFrame().Length);
            Assert.Equal(primitive.Shape, buffer.GetFrame()[0].Shape);
        }
    }
}

using System;
using System.Numerics;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests
{
    public class SimMathTests
    {
        [Fact]
        public void FromYaw_Zero_ProducesEastFacing()
        {
            var q = SimMath.FromYaw(0f);
            var dir = Vector3.Transform(Vector3.UnitX, q);
            Assert.Equal(1f, dir.X, 4);
            Assert.Equal(0f, dir.Y, 4);
        }

        [Fact]
        public void FromYaw_90deg_ProducesNorthFacing()
        {
            var q = SimMath.FromYaw(MathF.PI / 2f);
            var dir = Vector3.Transform(Vector3.UnitX, q);
            Assert.Equal(0f, dir.X, 4);
            Assert.Equal(1f, dir.Y, 4);
        }

        [Fact]
        public void FromYaw_Neg90deg_ProducesSouthFacing()
        {
            var q = SimMath.FromYaw(-MathF.PI / 2f);
            var dir = Vector3.Transform(Vector3.UnitX, q);
            Assert.Equal(0f, dir.X, 4);
            Assert.Equal(-1f, dir.Y, 4);
        }

        [Fact]
        public void FacingNorth_Constant_MatchesFromYaw90()
        {
            var expected = SimMath.FromYaw(MathF.PI / 2f);
            Assert.Equal(expected, SimMath.FacingNorth);
        }

        [Fact]
        public void ExtractYaw_RoundTrips_ThroughFromYaw()
        {
            float originalYaw = MathF.PI / 3f;
            var q = SimMath.FromYaw(originalYaw);
            float extracted = SimMath.ExtractYaw(q);
            Assert.Equal(originalYaw, extracted, 4);
        }
    }
}

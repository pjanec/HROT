using System;
using System.Numerics;
using CarKinem.Controllers;
using CarKinem.Core;
using Xunit;

namespace CarKinem.Tests.Algorithms
{
    public class BicycleModelTests
    {
        [Fact]
        public void BicycleModel_StraightMotion_UpdatesPosition()
        {
            var state = new VehicleState
            {
                Speed = 10f
            };
            var pos = Vector2.Zero;
            var fwd = new Vector2(1, 0);
            
            // Apply straight motion for 1 second
            BicycleModel.Integrate(
                ref pos,
                ref fwd,
                ref state,
                steerAngle: 0f,
                accel: 0f,
                dt: 1.0f,
                wheelBase: 2.7f
            );
            
            // After 1 second at 10 m/s moving along X (forward is 1,0)
            Assert.Equal(10f, pos.X, precision: 3);
            Assert.Equal(0f, pos.Y, precision: 3);
        }

        [Fact]
        public void BicycleModel_Turning_RotatesHeading()
        {
            var state = new VehicleState
            {
                Speed = 10f
            };
            var pos = Vector2.Zero;
            var fwd = new Vector2(1, 0);
            
            // Apply left steering for 1 second
            BicycleModel.Integrate(
                ref pos,
                ref fwd,
                ref state,
                steerAngle: 0.3f, // ~17 degrees
                accel: 0f,
                dt: 1.0f,
                wheelBase: 2.7f
            );
            
            // Heading should have rotated
            Assert.True(fwd.Y > 0f, "Should turn left (positive Y)");
            
            // Forward should still be normalized
            float length = fwd.Length();
            Assert.Equal(1f, length, precision: 4);
        }

        // STABILITY(Broken): BicycleModel.Integrate does not clamp speed to zero — returns -5 with accel=-10, dt=1; real bug in BicycleModel; investigate
        [Trait("Stability", "Broken")]
        [Fact]
        public void BicycleModel_NegativeSpeed_ClampsToZero()
        {
            var state = new VehicleState
            {
                Speed = 5f
            };
            var pos = Vector2.Zero;
            var fwd = new Vector2(1, 0);
            
            // Apply extreme braking
            BicycleModel.Integrate(
                pos: ref pos,
                fwd: ref fwd,
                state: ref state,
                steerAngle: 0f,
                accel: -10f, // Heavy deceleration
                dt: 1.0f,
                wheelBase: 2.7f
            );
            
            // Speed should be clamped to zero (no reverse)
            Assert.Equal(0f, state.Speed);
        }
        
        [Fact]
        public void BicycleModel_ZeroDt_NoChange()
        {
             var state = new VehicleState
            {
                Speed = 10f
            };
            var pos = Vector2.Zero;
            var fwd = new Vector2(1, 0);

            Vector2 initialPos = pos;
            Vector2 initialFwd = fwd;
            
            BicycleModel.Integrate(
                ref pos,
                ref fwd,
                ref state,
                steerAngle: 0.5f,
                accel: 10f,
                dt: 0f,
                wheelBase: 2.7f
            );
            
            Assert.Equal(initialPos, pos);
            Assert.Equal(initialFwd, fwd);
            Assert.Equal(10f, state.Speed); 
        }
    }
}

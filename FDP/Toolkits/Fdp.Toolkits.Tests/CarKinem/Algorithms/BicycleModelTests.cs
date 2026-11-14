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
        
        /// <summary>⭐ <c>CE-2167</c> (V5) — given a reverse speed it backs up, no faster than that, and the position goes backwards.</summary>
        [Fact]
        public void CE2167_BicycleModel_WithAReverseSpeed_BacksUp_ToThatSpeedOnly()
        {
            var state = new VehicleState { Speed = 0f };
            var pos = Vector2.Zero;
            var fwd = new Vector2(1, 0);
            for (int i = 0; i < 10; i++)
                BicycleModel.Integrate(ref pos, ref fwd, ref state, 0f, -10f, 0.5f, 2.7f, maxSpeedRev: 4f, maxSpeedFwd: 20f);
            Assert.Equal(-4f, state.Speed);
            Assert.True(pos.X < -10f, $"it moved backwards: {pos.X}");
            Assert.Equal(new Vector2(1, 0), fwd);
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

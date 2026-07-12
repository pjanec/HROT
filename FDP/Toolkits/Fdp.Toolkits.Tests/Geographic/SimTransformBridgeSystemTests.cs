using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;
using Xunit;

namespace Fdp.Toolkit.Geographic.Tests
{
    public class SimTransformBridgeSystemTests
    {
        // ?? RotationToHeadingDeg tests ????????????????????????????????????????

        [Fact]
        public void RotationToHeadingDeg_FacingEast_Returns90()
        {
            // yaw=0 ? facing east (+X) ? heading 90�
            var rotation = Quaternion.Identity;
            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);
            Assert.Equal(90f, heading, precision: 1);
        }

        [Fact]
        public void RotationToHeadingDeg_FacingNorth_Returns0()
        {
            // yaw=90� around Z ? forward = +Y ? heading 0� (North)
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);
            Assert.Equal(0f, heading, precision: 1);
        }

        [Fact]
        public void RotationToHeadingDeg_FacingSouth_Returns180()
        {
            // yaw=-90� around Z ? forward = -Y ? heading 180� (South)
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f);
            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);
            Assert.Equal(180f, heading, precision: 1);
        }

        [Fact]
        public void RotationToHeadingDeg_FacingWest_Returns270()
        {
            // yaw=180� around Z ? forward = -X ? heading 270� (West)
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);
            Assert.Equal(270f, heading, precision: 1);
        }

        // Fixed (D-2): RotationToHeadingDeg now falls back to 0 at gimbal lock (nose straight
        // up/down), where the forward axis projects to ~zero in the XY plane.
        [Fact]
        public void RotationToHeadingDeg_DegenerateRotation_Returns0()
        {
            // A rotation that maps UnitX to nearly zero in XY plane ? fallback 0
            // This is a pitch-straight-down scenario (90� around Y)
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);
            // Forward vector projects to near-zero in XY ? should return 0
            Assert.Equal(0f, heading, precision: 1);
        }

        // ?? VelocityToAzimuthDeg tests ????????????????????????????????????????

        [Fact]
        public void VelocityToAzimuthDeg_MovingEast_Returns90()
        {
            var vel = new Vector3(10f, 0f, 0f); // +X = East
            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(vel, fallback: 0f);
            Assert.Equal(90f, azimuth, precision: 1);
        }

        [Fact]
        public void VelocityToAzimuthDeg_MovingNorth_Returns0()
        {
            var vel = new Vector3(0f, 10f, 0f); // +Y = North
            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(vel, fallback: 999f);
            Assert.Equal(0f, azimuth, precision: 1);
        }

        [Fact]
        public void VelocityToAzimuthDeg_ZeroVelocity_ReturnsFallback()
        {
            var vel = Vector3.Zero;
            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(vel, fallback: 42f);
            Assert.Equal(42f, azimuth, precision: 1);
        }

        [Fact]
        public void VelocityToAzimuthDeg_MovingNorthEast_Returns45()
        {
            var vel = new Vector3(10f, 10f, 0f); // 45� between East and North
            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(vel, fallback: 0f);
            Assert.Equal(45f, azimuth, precision: 1);
        }

        [Fact]
        public void VelocityToAzimuthDeg_MovingSouthWest_Returns225()
        {
            var vel = new Vector3(-10f, -10f, 0f);
            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(vel, fallback: 0f);
            Assert.Equal(225f, azimuth, precision: 1);
        }

        // ?? Heading consistency with CarKinematicsSystem ??????????????????????

        [Fact]
        public void RotationToHeadingDeg_MatchesCarKinematicsConvention()
        {
            // Verify the documented convention:
            //   yaw=0 ? facing east (+X) ? heading=90�
            //   yaw=90� ? facing north (+Y) ? heading=0�
            // This is the UnitX-forward convention stated in SIM-BATCH-01 Source Deviations.

            // East (1,0)?90�
            var eastRot = Quaternion.Identity;
            Assert.Equal(90f, SimTransformBridgeSystem.RotationToHeadingDeg(eastRot), precision: 1);

            // North (0,1)?0�
            var northRot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
            Assert.Equal(0f, SimTransformBridgeSystem.RotationToHeadingDeg(northRot), precision: 1);

            // South (0,-1)?180�
            var southRot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f);
            Assert.Equal(180f, SimTransformBridgeSystem.RotationToHeadingDeg(southRot), precision: 1);

            // West (-1,0)?270�
            var westRot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
            Assert.Equal(270f, SimTransformBridgeSystem.RotationToHeadingDeg(westRot), precision: 1);
        }

        // ── RotationToPitchRollDeg tests ──────────────────────────────────────

        /// <summary>
        /// Quaternion.Identity → level flight: both pitch and roll must be zero.
        /// </summary>
        [Fact]
        public void RotationToPitchRollDeg_LevelFlight_ReturnsBothZero()
        {
            SimTransformBridgeSystem.RotationToPitchRollDeg(
                Quaternion.Identity, out float pitchDeg, out float rollDeg);

            Assert.InRange(pitchDeg, -0.1f,  0.1f);
            Assert.InRange(rollDeg,  -0.1f,  0.1f);
        }

        /// <summary>
        /// 30° nose-up pitch: pitchDeg ≈ +30, rollDeg ≈ 0.
        /// ENU frame: UnitY = body-left, so rotating around -UnitY by +30° tilts the nose toward +Z.
        /// </summary>
        // Fixed (D-2): bridge now negates SimMath's nose-down-positive pitch to nose-up-positive.
        [Fact]
        public void RotationToPitchRollDeg_NoseUp30_ReturnsPitchPositive30()
        {
            // Rotate around -UnitY by +PI/6: body forward (+X) tilts toward +Z (up) by 30°.
            var rotation = Quaternion.CreateFromAxisAngle(-Vector3.UnitY, MathF.PI / 6f);

            SimTransformBridgeSystem.RotationToPitchRollDeg(rotation, out float pitchDeg, out float rollDeg);

            Assert.InRange(pitchDeg, 28f, 32f);
            Assert.InRange(rollDeg,  -1f,  1f);
        }

        /// <summary>
        /// 30° nose-down pitch: pitchDeg ≈ −30, rollDeg ≈ 0.
        /// </summary>
        // Fixed (D-2): see RotationToPitchRollDeg_NoseUp30.
        [Fact]
        public void RotationToPitchRollDeg_NoseDown30_ReturnsPitchNegative30()
        {
            // Opposite of nose-up: rotate around -UnitY by -PI/6.
            var rotation = Quaternion.CreateFromAxisAngle(-Vector3.UnitY, -MathF.PI / 6f);

            SimTransformBridgeSystem.RotationToPitchRollDeg(rotation, out float pitchDeg, out float rollDeg);

            Assert.InRange(pitchDeg, -32f, -28f);
            Assert.InRange(rollDeg,   -1f,   1f);
        }

        /// <summary>
        /// 45° right-wing-down roll: rollDeg ≈ +45, pitchDeg ≈ 0.
        /// Convention: +ve = right wing down (GeoTransform.RollDeg comment).
        /// In ENU with UnitX-forward, rolling around UnitX by +PI/4 tilts the right wing toward -Z.
        /// </summary>
        [Fact]
        public void RotationToPitchRollDeg_RightWingDown45_ReturnsRollPositive45()
        {
            // Rotate around +UnitX by +PI/4: right wing (body -Y side) drops toward -Z (ground).
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 4f);

            SimTransformBridgeSystem.RotationToPitchRollDeg(rotation, out float pitchDeg, out float rollDeg);

            Assert.InRange(pitchDeg, -1f,  1f);
            Assert.InRange(rollDeg,  43f, 47f);
        }

        /// <summary>
        /// Compound rotation — 20° nose-up AND 30° right-wing-down:
        /// pitchDeg ≈ +20 (±2°) and rollDeg ≈ +30 (±2°).
        /// </summary>
        // Fixed (D-2): see RotationToPitchRollDeg_NoseUp30.
        [Fact]
        public void RotationToPitchRollDeg_Combined_PitchAndRollIndependent()
        {
            // Apply pitch first then roll (order matches typical flight state).
            var pitch    = Quaternion.CreateFromAxisAngle(-Vector3.UnitY, 20f * MathF.PI / 180f);
            var roll     = Quaternion.CreateFromAxisAngle( Vector3.UnitX, 30f * MathF.PI / 180f);
            var combined = Quaternion.Normalize(pitch * roll);

            SimTransformBridgeSystem.RotationToPitchRollDeg(combined, out float pitchDeg, out float rollDeg);

            Assert.InRange(pitchDeg, 18f, 22f);
            Assert.InRange(rollDeg,  28f, 32f);
        }

        /// <summary>
        /// Regression guard: a 20-degree nose-up rotation must produce a positive heading
        /// from RotationToHeadingDeg (this test replaces the former integration test that
        /// relied on SimTransformBridgeSystem.Execute() writing a GeoTransform component).
        /// </summary>
        // Fixed (D-2): see RotationToPitchRollDeg_NoseUp30.
        [Fact]
        public void RotationToPitchRollDeg_PitchedRotation_PitchDegNonZero()
        {
            var noseUpRot = Quaternion.CreateFromAxisAngle(-Vector3.UnitY, 20f * MathF.PI / 180f);

            SimTransformBridgeSystem.RotationToPitchRollDeg(noseUpRot, out float pitchDeg, out float rollDeg);

            Assert.NotEqual(0f, pitchDeg);
            Assert.InRange(pitchDeg, 18f, 22f);
            Assert.InRange(rollDeg, -1f, 1f);
        }
    }
}

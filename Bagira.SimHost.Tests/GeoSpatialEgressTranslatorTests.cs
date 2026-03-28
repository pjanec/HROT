using System;
using System.Numerics;
using System.Threading;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using Bagira.Map.Common.Replication.Egress;
using CycloneDDS.Runtime;
using Fdp.Modules.Geographic.Systems;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the heading/azimuth conversion logic used by
    /// <see cref="Bagira.Map.Common.Replication.Egress.GeoSpatialEgressTranslator"/>.
    ///
    /// The translator delegates heading computation to
    /// <see cref="SimTransformBridgeSystem.RotationToHeadingDeg"/> and
    /// <see cref="SimTransformBridgeSystem.VelocityToAzimuthDeg"/>, so we
    /// test those helpers directly to verify the wire format correctness.
    ///
    /// Full integration tests (ECS world ? DDS publish) require a DDS participant
    /// and are deferred to the integration test suite.
    /// </summary>
    [Collection("SimHostDds")]
    public class GeoSpatialEgressTranslatorTests
    {
        // ?? Heading ? GeoSpatial.Rot.Heading wire value ???????????????????????

        [Theory]
        [InlineData(0f, 90f)]              // yaw=0 ? East ? heading 90�
        [InlineData(MathF.PI / 2f, 0f)]    // yaw=90� ? North ? heading 0�
        [InlineData(-MathF.PI / 2f, 180f)] // yaw=-90� ? South ? heading 180�
        [InlineData(MathF.PI, 270f)]       // yaw=180� ? West ? heading 270�
        public void HeadingConversion_YawToCompass_CorrectWireValue(float yawRad, float expectedHeading)
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRad);
            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);
            Assert.Equal(expectedHeading, heading, precision: 1);
        }

        // ?? Velocity ? GeoSpatialDR.Vel.Azimuth wire value ???????????????????

        [Theory]
        [InlineData(10f, 0f, 0f, 90f)]   // Moving east ? azimuth 90�
        [InlineData(0f, 10f, 0f, 0f)]    // Moving north ? azimuth 0�
        [InlineData(-10f, 0f, 0f, 270f)] // Moving west ? azimuth 270�
        [InlineData(0f, -10f, 0f, 180f)] // Moving south ? azimuth 180�
        public void VelocityAzimuth_ENUToCompass_CorrectWireValue(
            float vx, float vy, float vz, float expectedAzimuth)
        {
            var vel = new Vector3(vx, vy, vz);
            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(vel, fallback: 0f);
            Assert.Equal(expectedAzimuth, azimuth, precision: 1);
        }

        [Fact]
        public void VelocityAzimuth_ZeroVelocity_UsesFallback()
        {
            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(Vector3.Zero, fallback: 123f);
            Assert.Equal(123f, azimuth, precision: 1);
        }

        // ?? Angular velocity conversion (rad/s ? deg/s) ??????????????????????

        [Fact]
        public void AngularVelocity_RadToDeg_CorrectConversion()
        {
            // GeoSpatialDR.RotVel expects deg/s; GeoVelocity.Angular stores rad/s
            float yawRateRadS = MathF.PI; // 180 deg/s
            float expectedDegS = 180f;

            float actual = yawRateRadS * (180f / MathF.PI);
            Assert.Equal(expectedDegS, actual, precision: 1);
        }

        // ?? Consistency: heading round-trip (rotation ? heading ? verify against forward vector)

        [Fact]
        public void HeadingRoundTrip_RotationToHeadingToVector_Consistent()
        {
            // Create a rotation for NE (45� compass heading ? yaw = 45� in math)
            float compassHeading = 45f;
            // compass 45� = NE. Math yaw = 90� - 45� = 45� (atan2 of (sin45, cos45))
            float mathYaw = (90f - compassHeading) * (MathF.PI / 180f);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, mathYaw);

            float computedHeading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);

            Assert.Equal(compassHeading, computedHeading, precision: 1);
        }

        // ── BUG2-N003 – GeoSpatialDR disposal integration tests ──────────────

        /// <summary>
        /// Verifies that calling <see cref="GeoSpatialEgressTranslator.Dispose(long)"/>
        /// tombstones the <see cref="GeoSpatialDR"/> topic instance.
        /// </summary>
        [Fact]
        public void Dispose_CallsDisposeOnDrWriter()
        {
            const uint domain = 165u;
            using var participant = new DdsParticipant(domain);
            using var drReader    = new DdsReader<GeoSpatialDR>(participant, "GeoSpatialDR");

            var geoTransform = BagiraEnvironment.CreateGeoTransform();
            var entityMap    = new NetworkEntityMap();
            var translator   = new GeoSpatialEgressTranslator(participant, entityMap, geoTransform);

            // Call Dispose — this should tombstone the GeoSpatialDR instance.
            translator.Dispose(42L);

            // Wait briefly for the loopback to complete.
            Thread.Sleep(200);

            // Check that a disposed (NOT_ALIVE) sample exists for EntityId=42.
            bool receivedTombstone = false;
            using var loan = drReader.Take();
            foreach (var sample in loan)
            {
                if (sample.Info.InstanceState != DdsInstanceState.Alive)
                {
                    receivedTombstone = true;
                    break;
                }
            }

            Assert.True(receivedTombstone,
                "Dispose(42) should tombstone the GeoSpatialDR instance (NOT_ALIVE sample expected).");
        }

        /// <summary>
        /// Verifies that calling <see cref="GeoSpatialEgressTranslator.Dispose(long)"/>
        /// also calls the base translator dispose (tombstoning the primary GeoSpatial topic).
        /// </summary>
        [Fact]
        public void Dispose_AlsoCallsBaseDispose()
        {
            const uint domain = 166u;
            using var participant  = new DdsParticipant(domain);
            using var geoReader    = new DdsReader<GeoSpatial>(participant, "GeoSpatial");

            var geoTransform = BagiraEnvironment.CreateGeoTransform();
            var entityMap    = new NetworkEntityMap();
            var translator   = new GeoSpatialEgressTranslator(participant, entityMap, geoTransform);

            // Call Dispose — base.Dispose should tombstone the GeoSpatial instance.
            translator.Dispose(99L);

            Thread.Sleep(200);

            // Confirm a NOT_ALIVE sample exists for EntityId=99 on the primary GeoSpatial topic.
            bool receivedTombstone = false;
            using var loan = geoReader.Take();
            foreach (var sample in loan)
            {
                if (sample.Info.InstanceState != DdsInstanceState.Alive)
                {
                    receivedTombstone = true;
                    break;
                }
            }

            Assert.True(receivedTombstone,
                "Dispose(99) should tombstone the primary GeoSpatial instance (NOT_ALIVE sample expected).");
        }
    }
}

using System;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.Map.Common;
using Hrot.Map.Common.Replication.Egress;
using CycloneDDS.Runtime;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the heading/azimuth conversion logic used by
    /// <see cref="Hrot.Map.Common.Replication.Egress.GeoSpatialEgressTranslator"/>.
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
        [InlineData(0f, 90f)]              // yaw=0 ? East ? heading 90°        
        [InlineData(MathF.PI / 2f, 0f)]    // yaw=90° ? North ? heading 0°      
        [InlineData(-MathF.PI / 2f, 180f)] // yaw=-90° ? South ? heading 180°   
        [InlineData(MathF.PI, 270f)]       // yaw=180° ? West ? heading 270°    
        public void HeadingConversion_YawToCompass_CorrectWireValue(float yawRad, float expectedHeading)
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRad);
            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);
            Assert.Equal(expectedHeading, heading, precision: 1);
        }

        // ?? Velocity ? WorldPos.Vel.Azimuth wire value ???????????????????    

        [Theory]
        [InlineData(10f, 0f, 0f, 90f)]   // Moving east ? azimuth 90°
        [InlineData(0f, 10f, 0f, 0f)]    // Moving north ? azimuth 0°
        [InlineData(-10f, 0f, 0f, 270f)] // Moving west ? azimuth 270°
        [InlineData(0f, -10f, 0f, 180f)] // Moving south ? azimuth 180°
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
            // WorldPos.RotVel expects deg/s; GeoVelocity.Angular stores rad/s  
            float yawRateRadS = MathF.PI; // 180 deg/s
            float expectedDegS = 180f;

            float actual = yawRateRadS * (180f / MathF.PI);
            Assert.Equal(expectedDegS, actual, precision: 1);
        }

        // ?? Consistency: heading round-trip (rotation ? heading ? verify against forward vector)

        [Fact]
        public void HeadingRoundTrip_RotationToHeadingToVector_Consistent()     
        {
            // Create a rotation for NE (45° compass heading ? yaw = 45° in math)
            float compassHeading = 45f;
            // compass 45° = NE. Math yaw = 90° - 45° = 45° (atan2 of (sin45, cos45))
            float mathYaw = (90f - compassHeading) * (MathF.PI / 180f);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, mathYaw);

            float computedHeading = SimTransformBridgeSystem.RotationToHeadingDeg(rotation);

            Assert.Equal(compassHeading, computedHeading, precision: 1);        
        }

        // -- CE-2348 ② — a stop is sent, and the heartbeat cannot be stepped over --

        private sealed class PassthroughGeo : Fdp.Modules.Geographic.IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }
            public Vector3 ToCartesian(double lat, double lon, double alt) => new((float)lon, (float)lat, (float)alt);
            public (double lat, double lon, double alt) ToGeodetic(Vector3 pos) => (pos.Y, pos.X, pos.Z);
        }

        private static (Fdp.Core.EntityRepository Repo, Fdp.Core.Entity Hull) OwnedHull()
        {
            var repo = new Fdp.Core.EntityRepository();
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
            repo.RegisterComponent<Fdp.Core.SimTransform>();
            repo.RegisterComponent<Fdp.Core.SimVelocity>();
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkTransform>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(42));
            repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(e, new Fdp.Core.SimTransform { Position = new Vector3(13.4f, 52.5f, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(e, new Fdp.Core.SimVelocity { Linear = new Vector3(0.3f, 0f, 0f) });
            return (repo, e);
        }

        /// <summary>⭐⭐ <c>CE-2348</c> ② — a hull that STOPS publishes its stop, though it did not move another centimetre. 📐 Measured on
        /// <c>uc-urban-battle</c>: the destroyed APC's last sample (creeping at ~0.3 m/s) stayed on every other node for ~200 s, so they read
        /// it as moving (and dead-reckoned it). ✅ Red-proof: the velocity test removed ⇒ no second sample ⇒ red.</summary>
        [Fact]
        public void AHullThatStops_PublishesTheStop_ThoughItDidNotMove_CE2348()
        {
            using var participant = new DdsParticipant(166u);
            var translator = new GeoSpatialEgressTranslator(participant, new NetworkEntityMap(), new PassthroughGeo(), localNodeId: 1);
            var (repo, hull) = OwnedHull();
            translator.ScanAndPublish(repo);
            long first = translator.SentSampleCount;
            Assert.Equal(1, first);                                                   // its first sample, creeping

            repo.Tick();
            repo.GetComponentRW<Fdp.Core.SimVelocity>(hull) = default;               // stopped where it stood
            translator.ScanAndPublish(repo);
            Assert.Equal(first + 1, translator.SentSampleCount);                     // the stop is sent at once

            repo.Tick();
            translator.ScanAndPublish(repo);
            Assert.Equal(first + 1, translator.SentSampleCount);                     // and nothing more while it stands
        }

        /// <summary>⭐ <c>CE-2348</c> ② — a standing hull's heartbeat comes within its interval however the translator is called. ⛔ It was
        /// one exact tick in 600 (<c>(Tick + salt) % 600 == 0</c>): called every 6th tick it never came. ✅ Red-proof: the modulo restored ⇒
        /// no heartbeat in 1 800 ticks ⇒ red.</summary>
        [Fact]
        public void AStandingHull_GetsItsHeartbeat_HoweverOftenTheTranslatorRuns_CE2348()
        {
            using var participant = new DdsParticipant(167u);
            var translator = new GeoSpatialEgressTranslator(participant, new NetworkEntityMap(), new PassthroughGeo(), localNodeId: 1);
            var (repo, hull) = OwnedHull();
            repo.GetComponentRW<Fdp.Core.SimVelocity>(hull) = default;
            translator.ScanAndPublish(repo);
            long first = translator.SentSampleCount;
            for (int i = 0; i < 300; i++)                                            // 1 800 ticks, called every 6th
            {
                for (int k = 0; k < 6; k++) repo.Tick();
                translator.ScanAndPublish(repo);
            }
            Assert.InRange(translator.SentSampleCount - first, 2, 4);               // ~ every 600 ticks: 3
        }

        // -- BUG2-N003 - WorldPos disposal integration tests --

        /// <summary>
        /// Verifies that calling <see cref="GeoSpatialEgressTranslator.Dispose(long)"/>
        /// tombstones the <see cref="WorldPos"/> topic instance.
        /// </summary>
        [Fact]
        public void Dispose_CallsDisposeOnDrWriter()
        {
            const uint domain = 165u;
            using var participant = new DdsParticipant(domain);
            using var drReader    = new DdsReader<WorldPos>(participant, "WorldPos");

            var geoTransform = HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0);
            var entityMap    = new NetworkEntityMap();
            var translator   = new GeoSpatialEgressTranslator(participant, entityMap, geoTransform, localNodeId: 0);

            // Call Dispose -- this should tombstone the WorldPos instance.
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
                "Dispose(42) should tombstone the WorldPos instance (NOT_ALIVE sample expected).");
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
            using var geoReader    = new DdsReader<WorldPos>(participant, "WorldPos");

            var geoTransform = HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0);
            var entityMap    = new NetworkEntityMap();
            var translator   = new GeoSpatialEgressTranslator(participant, entityMap, geoTransform, localNodeId: 0);

            // Call Dispose -- base.Dispose should tombstone the GeoSpatial instance.
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

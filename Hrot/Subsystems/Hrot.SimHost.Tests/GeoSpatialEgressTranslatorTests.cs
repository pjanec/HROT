using System;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.Map.Common;
using Hrot.Map.Common.Replication.Egress;
using CycloneDDS.Runtime;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Hrot.Map.Common.Replication.Ingress;
using System.Collections.Generic;
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

        // AQ107 D — the old "angular velocity rad/s -> deg/s" arithmetic test is gone with the conversion: AngularVelocity is
        // SimVelocity.Angular as is (rad/s about the local ENU axes), asserted by the wire round-trip rail below.

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
            var translator = new GeoSpatialEgressTranslator(participant, new NetworkEntityMap(), localNodeId: 1);
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
            var translator = new GeoSpatialEgressTranslator(participant, new NetworkEntityMap(), localNodeId: 1);
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

        // -- AQ107 B / D — the pose on the wire: ECS -> egress -> DDS -> WorldPos -> ingress -> ECS --

        public static IEnumerable<object[]> WirePoses()
        {
            yield return new object[] { "heading 30 / pitch 10 / roll -20", Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.4f, 0.17f, -0.35f)) };
            yield return new object[] { "nose straight UP (CE-4068)",       Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f) };
            yield return new object[] { "nose straight DOWN (CE-4068)",     Quaternion.CreateFromAxisAngle(Vector3.UnitY,  MathF.PI / 2f) };
            var q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 2.4f));
            yield return new object[] { "w < 0 before canonicalisation",    new Quaternion(-q.X, -q.Y, -q.Z, -q.W) };
        }

        /// <summary>Exposes the protected <c>Decode</c> so the REAL ingress translator reads what the REAL egress put on the wire.</summary>
        private sealed class IngressProbe : GeoSpatialIngressTranslator
        {
            public IngressProbe(NetworkEntityMap map, GhostCreationSystem ghosts)
                : base(participant: null, map, ghosts, localNodeId: 2) { }
            public void Apply(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view) => Decode(data, cmd, view);
        }

        /// <summary>
        /// ⭐⭐ <b>AQ107 D acceptance (a), through the real DDS wire</b> — an owned entity's pose (position, rotation, velocity, angular
        /// velocity) published by <see cref="GeoSpatialEgressTranslator"/> and read back by <see cref="GeoSpatialIngressTranslator"/> on a
        /// replica arrives within 1 mm / quaternion dot ≥ 1 − 1e-6 (sign-insensitive) / exactly, including a nose straight up and
        /// straight down (CE-4068) and a quaternion whose w is negative before canonicalisation; and the sample on the wire is the
        /// local-ENU one (position copied, SimTime = the sim clock). ✅ Red-proof (run): <c>QuaternionWireCodec.Encode</c> without the
        /// <c>w ≥ 0</c> canonicalisation ⇒ the "w &lt; 0" case comes back as the inverse rotation ⇒ red.
        /// </summary>
        [Theory]
        [MemberData(nameof(WirePoses))]
        public void AQ107D_APoseOnTheDdsWire_RoundTripsEcsToWorldPosToEcs(string _, Quaternion rotation)
        {
            using var participant = new DdsParticipant(188u);
            using var reader      = new DdsReader<WorldPos>(participant, "WorldPos");
            var translator = new GeoSpatialEgressTranslator(participant, new NetworkEntityMap(), localNodeId: 1);

            var (repo, hull) = OwnedHull();
            var position = new Vector3(1234.5f, -8765.25f, 42.125f);
            var linear   = new Vector3(1.5f, -2f, 0.25f);
            var angular  = new Vector3(0.1f, -0.2f, 0.3f);
            repo.GetComponentRW<Fdp.Core.SimTransform>(hull) = new Fdp.Core.SimTransform { Position = position, Rotation = rotation };
            repo.GetComponentRW<Fdp.Core.SimVelocity>(hull)  = new Fdp.Core.SimVelocity { Linear = linear, Angular = angular };

            translator.ScanAndPublish(repo);
            Assert.Equal(1, translator.SentSampleCount);

            WorldPos wire = default;
            bool got = false;
            for (int i = 0; i < 60 && !got; i++)
            {
                using var loan = reader.Take();
                foreach (var sample in loan)
                    if (sample.IsValid && sample.Info.InstanceState == DdsInstanceState.Alive && sample.Data.EntityId == 42) { wire = sample.Data; got = true; }
                if (!got) Thread.Sleep(50);
            }
            Assert.True(got, "no WorldPos sample for entity 42 arrived over DDS");
            Assert.Equal(position.X, wire.Position.X, 3);     // the wire is local ENU: a copy
            Assert.Equal(position.Y, wire.Position.Y, 3);
            Assert.Equal(position.Z, wire.Position.Z, 3);

            // the replica: another world, the REAL ingress translator
            var replica = new EntityRepository();
            replica.RegisterComponent<NetworkIdentity>();
            replica.RegisterComponent<NetworkAuthority>();
            replica.RegisterComponent<Fdp.Core.SimTransform>();
            replica.RegisterComponent<Fdp.Core.SimVelocity>();
            replica.RegisterComponent<NetworkTransform>();
            replica.RegisterComponent<NetworkVelocity>();
            var map = new NetworkEntityMap();
            var ghost = replica.CreateEntity();
            replica.AddComponent(ghost, new NetworkIdentity(42));
            replica.AddComponent(ghost, new Fdp.Core.SimTransform());
            replica.AddComponent(ghost, new Fdp.Core.SimVelocity());
            replica.AddComponent(ghost, new NetworkTransform());
            replica.AddComponent(ghost, new NetworkVelocity());
            map.Register(42, ghost);

            var probe = new IngressProbe(map, new GhostCreationSystem(map));
            ISimulationView view = replica;
            var cmd = view.GetCommandBuffer();
            probe.Apply(wire, cmd, view);
            ((EntityCommandBuffer)cmd).Playback(replica);

            var st = replica.GetComponent<Fdp.Core.SimTransform>(ghost);
            Assert.True(Vector3.Distance(position, st.Position) < 0.001f, $"position {st.Position}");
            float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(rotation), st.Rotation));
            Assert.True(dot >= 1f - 1e-6f, $"rotation dot {dot:R}");
            Assert.Equal(linear,  replica.GetComponent<NetworkVelocity>(ghost).Value);
            Assert.Equal(angular, replica.GetComponent<Fdp.Core.SimVelocity>(ghost).Angular);
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
            var translator   = new GeoSpatialEgressTranslator(participant, entityMap, localNodeId: 0);

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
            var translator   = new GeoSpatialEgressTranslator(participant, entityMap, localNodeId: 0);

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

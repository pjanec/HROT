using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Bagira.Map.Common.Replication.Egress
{
    /// <summary>
    /// Reads <see cref="SimTransform"/> + <see cref="SimVelocity"/> ECS components,
    /// converts them to geodetic coordinates on-the-fly via <see cref="IGeographicTransform"/>,
    /// and publishes <see cref="GeoSpatial"/> / <see cref="GeoSpatialDR"/> DDS topics.
    /// </summary>
    public class GeoSpatialEgressTranslator : CycloneTranslator<GeoSpatial, GeoSpatial>
    {
        private readonly DdsWriter<GeoSpatialDR> _drWriter;
        private readonly IGeographicTransform _geoTransform;
        private readonly HashSet<long> _tracedNetIds = new();

        private const long GeoSpatialOrdinal = (long)Bagira.BDC.SSTD.EDescriptorType.dtGeoSpatial;

        public GeoSpatialEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform)
            : base(participant, "GeoSpatial", ordinal: GeoSpatialOrdinal, entityMap)
        {
            _drWriter = new DdsWriter<GeoSpatialDR>(participant, "GeoSpatialDR");
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        /// <summary>
        /// Inbound decode is not used for authority nodes.
        /// </summary>
        protected override void Decode(in GeoSpatial data, IEntityCommandBuffer cmd, ISimulationView view)
        {
        }

        /// <summary>
        /// Tombstones both the primary <see cref="GeoSpatial"/> topic instance (via base)
        /// and the secondary <see cref="GeoSpatialDR"/> instance to prevent descriptor leaks.
        /// </summary>
        public override void Dispose(long networkEntityId)
        {
            base.Dispose(networkEntityId);
            _drWriter.DisposeInstance(new GeoSpatialDR { EntityId = (int)networkEntityId });
        }

        /// <summary>
        /// Scans all locally-owned entities with <see cref="SimTransform"/> and publishes
        /// <see cref="GeoSpatial"/> (position/orientation) and <see cref="GeoSpatialDR"/>
        /// (velocity/acceleration) to DDS, converting Cartesian to geodetic on the fly.
        ///
        /// <para>
        /// Change-detection is performed by comparing the live <see cref="SimTransform"/> against
        /// the <see cref="NetworkTransform"/> shadow component, which stores the position and
        /// rotation that were last sent to the network.  A packet is sent when:
        /// <list type="bullet">
        ///   <item>The entity has moved more than 1 cm² (Position threshold).</item>
        ///   <item>The entity has rotated by more than ~0.5° (Quaternion dot threshold).</item>
        ///   <item>A salted 600-tick heartbeat fires (UDP loss recovery).</item>
        /// </list>
        /// This bypass of <c>SmartEgressUtil</c> keeps the hot path entirely in unmanaged
        /// memory and avoids the Dictionary/HashSet lookups of <see cref="EgressPublicationState"/>.
        /// </para>
        /// </summary>
        public override void ScanAndPublish(ISimulationView view)
        {
            // GetComponentRW requires EntityRepository (concrete write access).
            // ScanAndPublish is only called from the egress system which always
            // supplies the live world; bail out safely if this ever changes.
            if (view is not EntityRepository repo) return;

            // Only entities that have a NetworkTransform shadow can participate in
            // change-detection egress.  Entities spawned through BdcTkbBuilder always
            // receive this component; older/test entities without it are skipped.
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkTransform>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            const float PositionThresholdSq = 0.0001f; // 1 cm²  — avoids spurious sends from float noise
            const float RotationDotThreshold = 0.9999f; // ~0.5° arc — Quaternion.Dot == 1 when identical
            const uint  HeartbeatInterval   = 600;      // 10 s at 60 Hz for UDP loss recovery

            foreach (var entity in query)
            {
                // Authority check: only publish if this node owns geospatial data for this entity.
                if (!view.HasAuthority(entity, DescriptorOrdinal))
                    continue;

                ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);
                ref var          netTf = ref repo.GetComponentRW<NetworkTransform>(entity);

                // Shadow comparison — entirely in unmanaged memory, no heap allocations.
                bool hasMoved   = Vector3.DistanceSquared(simTf.Position, netTf.LastPosition) > PositionThresholdSq;
                bool hasRotated = Math.Abs(Quaternion.Dot(simTf.Rotation, netTf.LastRotation)) < RotationDotThreshold;

                // Salted heartbeat: stagger entities by index so they don't all fire on tick 0.
                uint salt      = (uint)(entity.Index % HeartbeatInterval);
                bool heartbeat = ((view.Tick + salt) % HeartbeatInterval) == 0;

                if (!hasMoved && !hasRotated && !heartbeat)
                    continue;

                // Update shadow before publishing so the next tick comparison is against the
                // just-sent values, not stale pre-move values.
                netTf.LastPosition = simTf.Position;
                netTf.LastRotation = simTf.Rotation;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // Direct conversion: SimTransform (Cartesian) → GeoSpatial (Geodetic)
                var (lat, lon, alt) = _geoTransform.ToGeodetic(simTf.Position);
                float heading = SimTransformBridgeSystem.RotationToHeadingDeg(simTf.Rotation);
                SimTransformBridgeSystem.RotationToPitchRollDeg(simTf.Rotation, out float pitch, out float roll);

                Publish(new GeoSpatial
                {
                    EntityId = (int)netId.Value,
                    Time     = DateTime.UtcNow,
                    Pos = new GeoPosition
                    {
                        Latitude  = lat,
                        Longitude = lon,
                        Altitude  = alt,
                    },
                    Rot = new OrientationHPR
                    {
                        Heading = heading,
                        Pitch   = pitch,
                        Roll    = roll,
                    },
                });

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<GeoSpatialEgressTranslator>.Debug(
                        "[TRACE-SH] Egress: Writing GeoSpatial for NetID={0} pos=({1},{2})", netId.Value, lat, lon);
                }

                if (view.HasComponent<SimVelocity>(entity))
                {
                    ref readonly var simVel = ref view.GetComponentRO<SimVelocity>(entity);

                    // Convert ENU linear velocity to DAL3 (azimuth/elevation/speed)
                    var velDAL3 = EnuToDAL3(simVel.Linear, heading);
                    var accDAL3 = new DAL3 { Azimuth = heading, Elevation = 0f, Length = 0f };

                    _drWriter.Write(new GeoSpatialDR
                    {
                        EntityId = (int)netId.Value,
                        Time     = DateTime.UtcNow,
                        Vel      = velDAL3,
                        Acc      = accDAL3,
                        RotVel = new OrientationHPR
                        {
                            // Angular velocity: rad/s to deg/s
                            // simVel.Angular: X=roll-rate, Y=pitch-rate, Z=yaw-rate
                            Heading = simVel.Angular.Z * (180f / MathF.PI),
                            Pitch   = simVel.Angular.Y * (180f / MathF.PI),
                            Roll    = simVel.Angular.X * (180f / MathF.PI),
                        },
                    });
                }
            }
        }

        /// <inheritdoc/>
        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
        }

        /// <summary>
        /// Converts an ENU vector (X=East, Y=North, Z=Up) to <see cref="DAL3"/>
        /// (Azimuth=compass heading, Elevation=pitch, Length=magnitude).
        /// </summary>
        private static DAL3 EnuToDAL3(Vector3 enu, float fallbackAzimuth)
        {
            float length = enu.Length();
            if (length < 1e-4f)
                return new DAL3 { Azimuth = fallbackAzimuth, Elevation = 0f, Length = 0f };

            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(enu, fallbackAzimuth);
            float elevation = MathF.Asin(Math.Clamp(enu.Z / length, -1f, 1f)) * (180f / MathF.PI);

            return new DAL3
            {
                Azimuth   = azimuth,
                Elevation = elevation,
                Length    = length,
            };
        }
    }
}

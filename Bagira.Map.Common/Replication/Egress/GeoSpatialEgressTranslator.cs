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
using FDP.Toolkit.Replication.Utilities;
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

        public GeoSpatialEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform)
            : base(participant, "GeoSpatial", ordinal: 10, entityMap)
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
        /// Scans all locally-owned entities with <see cref="SimTransform"/> and publishes
        /// <see cref="GeoSpatial"/> (position/orientation) and <see cref="GeoSpatialDR"/>
        /// (velocity/acceleration) to DDS, converting Cartesian to geodetic on the fly.
        /// </summary>
        public override void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                // Authority check: only publish if this node owns geospatial data for this entity.
                if (!view.HasAuthority(entity, DescriptorOrdinal))
                    continue;

                // Smart egress: GeoSpatial uses unreliable (UDP) transport, so we apply
                // heartbeat refresh to recover from packet loss.
                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: true))
                    continue;

                ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);
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

                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
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

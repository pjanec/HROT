using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic.Components;
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
    /// Reads <see cref="GeoTransform"/> + <see cref="GeoVelocity"/> ECS components
    /// and publishes <see cref="GeoSpatial"/> / <see cref="GeoSpatialDR"/> DDS topics.
    /// </summary>
    public class GeoSpatialEgressTranslator : CycloneTranslator<GeoSpatial, GeoSpatial>
    {
        private readonly DdsWriter<GeoSpatialDR> _drWriter;
        private readonly HashSet<long> _tracedNetIds = new();

        public GeoSpatialEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap)
            : base(participant, "GeoSpatial", ordinal: 10, entityMap)
        {
            _drWriter = new DdsWriter<GeoSpatialDR>(participant, "GeoSpatialDR");
        }

        /// <summary>
        /// Inbound decode is not used for authority nodes.
        /// </summary>
        protected override void Decode(in GeoSpatial data, IEntityCommandBuffer cmd, ISimulationView view)
        {
        }

        /// <summary>
        /// Scans all locally-owned entities with <see cref="GeoTransform"/> and publishes
        /// <see cref="GeoSpatial"/> (position/orientation) and <see cref="GeoSpatialDR"/>
        /// (velocity/acceleration) to DDS.
        /// </summary>
        public override void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<GeoTransform>()
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

                ref readonly var geoTf = ref view.GetComponentRO<GeoTransform>(entity);
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                var latitude = geoTf.Latitude;
                var longitude = geoTf.Longitude;

                Publish(new GeoSpatial
                {
                    EntityId = (int)netId.Value,
                    Time     = DateTime.UtcNow,
                    Pos = new GeoPosition
                    {
                        Latitude  = latitude,
                        Longitude = longitude,
                        Altitude  = geoTf.Altitude,
                    },
                    Rot = new OrientationHPR
                    {
                        Heading = geoTf.HeadingDeg,
                        Pitch   = geoTf.PitchDeg,
                        Roll    = geoTf.RollDeg,
                    },
                });

                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);

                if (_tracedNetIds.Add(netId.Value))
                {
                    var posLabel = string.Concat(latitude, ",", longitude);
                    FdpLog<GeoSpatialEgressTranslator>.Debug(
                        "[TRACE-SH] Egress: Writing GeoSpatial for NetID={0} pos=({1})", netId.Value, posLabel);
                }

                if (view.HasComponent<GeoVelocity>(entity))
                {
                    ref readonly var geoVel = ref view.GetComponentRO<GeoVelocity>(entity);

                    // Convert ENU linear velocity to DAL3 (azimuth/elevation/speed)
                    var velDAL3 = EnuToDAL3(geoVel.Linear, geoTf.HeadingDeg);
                    var accDAL3 = EnuToDAL3(geoVel.Accel, geoTf.HeadingDeg);

                    _drWriter.Write(new GeoSpatialDR
                    {
                        EntityId = (int)netId.Value,
                        Time     = DateTime.UtcNow,
                        Vel      = velDAL3,
                        Acc      = accDAL3,
                        RotVel = new OrientationHPR
                        {
                            // Angular velocity: rad/s to deg/s
                            // geoVel.Angular: X=roll-rate, Y=pitch-rate, Z=yaw-rate
                            Heading = geoVel.Angular.Z * (180f / MathF.PI),
                            Pitch   = geoVel.Angular.Y * (180f / MathF.PI),
                            Roll    = geoVel.Angular.X * (180f / MathF.PI),
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

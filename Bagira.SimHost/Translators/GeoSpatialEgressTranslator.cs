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
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Network.Cyclone.Translators;

namespace Bagira.SimHost.Translators
{
    /// <summary>
    /// Reads <see cref="GeoTransform"/> + <see cref="GeoVelocity"/> ECS components
    /// and publishes <see cref="GeoSpatial"/> / <see cref="GeoSpatialDR"/> DDS topics.
    ///
    /// This is a thin, application-layer egress translator � it keeps
    /// <c>Bagira.BDC.SSTD</c> types out of the shared Geographic toolkit.
    /// Same pattern as <c>FastGeodeticTranslator</c> in NetworkDemo.
    ///
    /// The IG will later register the same toolkit module and add its own
    /// IG-specific egress translator with zero changes to the toolkit.
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
        /// Inbound decode � not used by SimHost (authority node).
        /// SimHost owns GeoTransform; remote updates come via GeodeticSmoothingSystem.
        /// </summary>
        protected override void Decode(in GeoSpatial data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            // SimHost is the authority � inbound GeoSpatial is not expected.
            // If needed for multi-SimHost scenarios, add ingress here.
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
                .With<NetworkOwnership>()
                .WithLifecycle(Fdp.Kernel.EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                // Only publish if we are the primary owner
                ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                    continue;

                ref readonly var geoTf = ref view.GetComponentRO<GeoTransform>(entity);
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // ?? GeoSpatial ????????????????????????????????????????????????
                Publish(new GeoSpatial
                {
                    EntityId = (int)netId.Value,
                    Time     = DateTime.UtcNow,
                    Pos = new GeoPosition
                    {
                        Latitude  = geoTf.Latitude,
                        Longitude = geoTf.Longitude,
                        Altitude  = geoTf.Altitude,
                    },
                    Rot = new OrientationHPR
                    {
                        Heading = geoTf.HeadingDeg,
                        Pitch   = geoTf.PitchDeg,
                        Roll    = geoTf.RollDeg,
                    },
                });

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<GeoSpatialEgressTranslator>.Debug(
                        "[TRACE-SH] Egress: Writing GeoSpatial for NetID={0} (Logging first publish only)", netId.Value);
                }

                // ?? GeoSpatialDR ??????????????????????????????????????????????
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
                            // Angular velocity: rad/s ? deg/s
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
            // Not used � SimHost is the authority for GeoSpatial.
        }

        // ?? Helpers ???????????????????????????????????????????????????????????

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

using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;
using Fdp.Interfaces;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Hrot <c>WorldPos</c> DDS topic.
    /// Converts geodetic coordinates (lat/lon/alt) into <see cref="NetworkTransform"/>
    /// and polar velocity into <see cref="NetworkVelocity"/> in a single unified pass.
    /// </summary>
    public class GeoSpatialIngressTranslator : CycloneTranslator<WorldPos, WorldPos>
    {
        private const string DdsTopicName = "WorldPos";
        private const long OrdinalValue = (long)Hrot.NED.Descriptors.EDescriptorType.dtWorldPos;

        private readonly IGeographicTransform _geoTransform;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public GeoSpatialIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            GhostCreationSystem ghostCreationSystem,
            long localNodeId)
            : base(participant, DdsTopicName, OrdinalValue, entityMap)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = localNodeId;
        }

        protected override void Decode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!EntityMap.TryGetEntity(netId, out var entity))
            {
                var repo = view as EntityRepository;
                if (repo == null)
                {
                    FdpLog<GeoSpatialIngressTranslator>.Warn(
                        "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            var latitude  = data.Pos.Latitude;
            var longitude = data.Pos.Longitude;
            FdpLog<GeoSpatialIngressTranslator>.Debug(
                "[Node-{0}] Ingress: GeoSpatial Entity={1} Lat={2} Lon={3}", _localNodeId, entity.Index, latitude, longitude);

            // 1. Position & rotation
            var cartesian = _geoTransform.ToCartesian(
                data.Pos.Latitude, data.Pos.Longitude, data.Pos.Altitude);

            var position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z);
            var rotation = SimTransformBridgeSystem.HeadingDegToRotation(data.Ori.Heading);

            cmd.SetComponent(entity, new NetworkTransform { LastPosition = position, LastRotation = rotation });

            // Guard: do NOT override SimTransform for locally-owned entities.
            // In AllInOne / combined Brain+Muscle roles, DDS loopback causes this translator
            // to receive WorldPos samples published by the SAME node. Without this guard
            // the last-published position would be written back every frame via loopback,
            // undoing any position changes made by physics or drag operations.

            // 1. Primary Owner check (handles Brain-node loopback for entities whose Brain IS
            //    also the NetworkAuthority primary owner).
            bool isLocallyOwned = false;
            if (view.HasComponent<NetworkAuthority>(entity))
            {
                isLocallyOwned = view.GetComponentRO<NetworkAuthority>(entity).HasAuthority;
            }

            // 2. Granular Split-Authority check (handles Muscle-node loopback).
            //    When the Brain delegated dtWorldPos to the Muscle via DeferredTakeOwnership,
            //    the Muscle node holds an explicit DescriptorOwnership entry for this ordinal.
            //    Without this check the Muscle would overwrite its own live physics position
            //    with the stale loopback packet it just published, causing "shivering".
            if (!isLocallyOwned && view.HasManagedComponent<DescriptorOwnership>(entity))
            {
                var ownership = view.GetManagedComponentRO<DescriptorOwnership>(entity);
                long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(OrdinalValue, 0);
                if (ownership.Map.TryGetValue(packedKey, out int ownerNodeId))
                {
                    isLocallyOwned = (ownerNodeId == (int)_localNodeId);
                }
            }

            if (!isLocallyOwned)
            {
                cmd.SetComponent(entity, new SimTransform { Position = position, Rotation = rotation });

                // 2. Velocity (only meaningful for remote/ghost entities)
                float speedMs = (float)data.Vel.Length;
                float azimRad = (float)data.Vel.Azimuth   * (MathF.PI / 180f);
                float elevRad = (float)data.Vel.Elevation * (MathF.PI / 180f);

                var cartVel = new Vector3(
                    speedMs * MathF.Cos(elevRad) * MathF.Sin(azimRad),
                    speedMs * MathF.Cos(elevRad) * MathF.Cos(azimRad),
                    speedMs * MathF.Sin(elevRad));

                cmd.SetComponent(entity, new NetworkVelocity { Value = cartVel });
            }
        }

        public override void ScanAndPublish(ISimulationView view) { }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not WorldPos geo) return;

            // 1. Position & rotation
            var cartesian = _geoTransform.ToCartesian(geo.Pos.Latitude, geo.Pos.Longitude, geo.Pos.Altitude);
            var position  = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z);
            var rotation  = SimTransformBridgeSystem.HeadingDegToRotation(geo.Ori.Heading);

            repo.SetComponent(entity, new NetworkTransform { LastPosition = position, LastRotation = rotation });
            repo.SetComponent(entity, new SimTransform    { Position = position, Rotation = rotation });

            // 2. Velocity
            float speedMs = (float)geo.Vel.Length;
            float azimRad = (float)geo.Vel.Azimuth   * (MathF.PI / 180f);
            float elevRad = (float)geo.Vel.Elevation * (MathF.PI / 180f);

            var cartVel = new Vector3(
                speedMs * MathF.Cos(elevRad) * MathF.Sin(azimRad),
                speedMs * MathF.Cos(elevRad) * MathF.Cos(azimRad),
                speedMs * MathF.Sin(elevRad));

            repo.SetComponent(entity, new NetworkVelocity { Value = cartVel });
        }
    }
}

using System;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Utilities;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Systems
{
    /// <summary>
    /// Consumes <see cref="UpdateEntityDescriptorRequest"/> messages from DDS.
    ///
    /// <para>When this node holds authority over the requested descriptor
    /// (checked via <see cref="AuthorityExtensions.HasAuthority(ModuleHost.Core.Abstractions.ISimulationView, Entity, long)"/>),
    /// the ECS state is updated and the egress translator is hinted to publish
    /// immediately via <see cref="SmartEgressUtil.MarkDirty"/>.</para>
    ///
    /// <para>Currently handles <see cref="EDescriptorType.dtGeoSpatial"/>:
    /// converts the geodetic position carried in the request to local Cartesian
    /// coordinates and writes the result into <see cref="SimTransform"/>.
    /// The geographic bridge system converts <c>SimTransform</c> to
    /// <c>GeoTransform</c> on the same tick, so the egress translator picks up
    /// the updated position immediately.</para>
    ///
    /// <para>An <see cref="UpdateEntityDescriptorAck"/> is written for every
    /// processed sample regardless of outcome so the sender can correlate replies.</para>
    /// </summary>
    public sealed class UpdateEntityDescriptorRequestSystem : ComponentSystem
    {
        private const long GeoSpatialOrdinal = (long)Bagira.BDC.SSTD.EDescriptorType.dtGeoSpatial;

        private const int ErrorSuccess        = 0;
        private const int ErrorEntityNotFound = 2;
        private const int ErrorNotOwner       = 4;
        private const int ErrorNotSupported   = 6;

        private readonly DdsReader<UpdateEntityDescriptorRequest> _reader;
        private readonly DdsWriter<UpdateEntityDescriptorAck>     _ackWriter;
        private readonly NetworkEntityMap                         _entityMap;
        private readonly IGeographicTransform                     _geoTransform;

        /// <summary>
        /// Creates a new system instance.
        /// </summary>
        /// <param name="participant">DDS participant for topic subscriptions.</param>
        /// <param name="entityMap">Shared net-ID → entity lookup service.</param>
        /// <param name="geoTransform">
        /// Geographic transform used to convert incoming geodetic coordinates to
        /// local Cartesian space (must match the transform used by egress translators).
        /// </param>
        public UpdateEntityDescriptorRequestSystem(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)
        {
            _reader       = new DdsReader<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
            _ackWriter    = new DdsWriter<UpdateEntityDescriptorAck>(participant, "UpdateEntityDescriptorAck");
            _entityMap    = entityMap     ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform  ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        // ── ComponentSystem lifecycle ─────────────────────────────────────────

        protected override void OnUpdate()
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ProcessRequest(sample.Data);
            }
        }

        protected override void OnDestroy()
        {
            _reader.Dispose();
            _ackWriter.Dispose();
        }

        // ── Request handling ──────────────────────────────────────────────────

        private void ProcessRequest(UpdateEntityDescriptorRequest req)
        {
            // 1. Resolve entity from network ID.
            if (!_entityMap.TryGetEntity(req.EntityId, out var entity))
            {
                WriteAck(req.RequestId, req.EntityId, ErrorEntityNotFound);
                return;
            }

            switch (req.DescriptorType)
            {
                case EDescriptorType.dtGeoSpatial:
                    ProcessGeoSpatialUpdate(req, entity);
                    break;

                default:
                    FdpLog<UpdateEntityDescriptorRequestSystem>.Debug(
                        "[UpdDescReq] Ignoring unsupported DescriptorType {0} for Entity {1}.",
                        req.DescriptorType, req.EntityId);
                    WriteAck(req.RequestId, req.EntityId, ErrorNotSupported);
                    break;
            }
        }

        private void ProcessGeoSpatialUpdate(UpdateEntityDescriptorRequest req, Entity entity)
        {
            // 2. Authority guard — only apply if this node owns the GeoSpatial descriptor.
            var view = (ISimulationView)World;
            if (!view.HasAuthority(entity, GeoSpatialOrdinal))
            {
                FdpLog<UpdateEntityDescriptorRequestSystem>.Debug(
                    "[UpdDescReq] Not authoritative for GeoSpatial on Entity {0}. Ignoring.",
                    req.EntityId);
                WriteAck(req.RequestId, req.EntityId, ErrorNotOwner);
                return;
            }

            // 3. Convert geodetic → local Cartesian.
            var geo = req.Payload.GeoSpatial;
            var cartesian = _geoTransform.ToCartesian(
                geo.Pos.Latitude,
                geo.Pos.Longitude,
                geo.Pos.Altitude);

            // 4. Preserve existing rotation; update position only.
            var currentRot = view.HasComponent<SimTransform>(entity)
                ? view.GetComponentRO<SimTransform>(entity).Rotation
                : Quaternion.Identity;

            World.SetComponent(entity, new SimTransform
            {
                Position = cartesian,
                Rotation = currentRot,
            });

            // 5. Force immediate GeoSpatial egress on this tick rather than waiting
            //    for the next heartbeat window (GeoSpatial uses unreliable/UDP transport).
            SmartEgressUtil.MarkDirty(World, entity, GeoSpatialOrdinal);

            FdpLog<UpdateEntityDescriptorRequestSystem>.Info(
                "[UpdDescReq] Applied GeoSpatial move for NetID {0} → ({1:F1}, {2:F1}, {3:F1}) Cartesian.",
                req.EntityId, cartesian.X, cartesian.Y, cartesian.Z);

            WriteAck(req.RequestId, req.EntityId, ErrorSuccess);
        }

        private void WriteAck(Guid requestId, int entityId, int errorCode)
        {
            _ackWriter.Write(new UpdateEntityDescriptorAck
            {
                RequestId = requestId,
                EntityId  = entityId,
                ErrorCode = errorCode,
            });
        }
    }
}

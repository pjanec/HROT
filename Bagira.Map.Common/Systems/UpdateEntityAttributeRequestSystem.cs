using System;
using System.Collections.Generic;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.Map.Common.Replication.Utils;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using FDP.Toolkit.NetworkSpawning;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Utilities;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Systems
{
    using SstErrorCode = Bagira.BDC.SSTM.SstErrorCode;

    /// <summary>
    /// Consumes <see cref="UpdateEntityAttributeRequest"/> messages from DDS and applies
    /// fine-grained field-level patches to live ECS components without replacing a full descriptor.
    ///
    /// <para>
    /// This complements <see cref="UpdateEntityDescriptorRequestSystem"/>, which operates at the
    /// descriptor granularity.  Attribute updates are lighter-weight: the sender specifies only
    /// the specific field to change (<see cref="EntityAttribute"/>) rather than a complete
    /// descriptor snapshot.
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="EntityAttribute.eaName"/> → patches <c>IgEntityData.Name</c> and triggers
    ///     an immediate <c>dtEntityInfo</c> egress hint via <see cref="SmartEgressUtil.MarkDirty"/>.
    ///   </item>
    ///   <item>
    ///     <see cref="EntityAttribute.eaGeoPosition"/> → patches <see cref="SimTransform.Position"/>
    ///     while preserving existing rotation.  The GeoSpatial egress translator picks up the
    ///     change automatically on its next tick via shadow-component comparison;
    ///     <see cref="SmartEgressUtil.MarkDirty"/> is <i>not</i> called for geo position.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// A <see cref="CreateUpdateDeleteEntityAck"/> is always written for every processed sample
    /// so the originating IG can correlate the response.
    /// </para>
    /// </summary>
    public sealed class UpdateEntityAttributeRequestSystem : ComponentSystem
    {
        private const long EntityInfoOrdinal = (long)EDescriptorType.dtEntityInfo;

        private readonly DdsReader<UpdateEntityAttributeRequest> _reader;
        private readonly DdsWriter<CreateUpdateDeleteEntityAck>  _ackWriter;
        private readonly NetworkEntityMap                        _entityMap;
        private readonly IGeographicTransform?                   _geoTransform;

        /// <summary>
        /// Creates a new system instance.
        /// </summary>
        /// <param name="participant">DDS participant used for topic subscriptions and publications.</param>
        /// <param name="entityMap">Shared net-ID → entity lookup service.</param>
        /// <param name="geoTransform">
        /// Optional geographic transform.  Required to convert incoming geodetic coordinates
        /// to local Cartesian when <see cref="EntityAttribute.eaGeoPosition"/> attributes arrive.
        /// When <c>null</c>, geo-position patches are silently skipped with
        /// <see cref="SstErrorCode.NotSupported"/>.
        /// </param>
        public UpdateEntityAttributeRequestSystem(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform? geoTransform)
        {
            _reader       = new DdsReader<UpdateEntityAttributeRequest>(participant, "UpdateEntityAttributeRequest");
            _ackWriter    = new DdsWriter<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");
            _entityMap    = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform;
        }

        // ── ComponentSystem lifecycle ──────────────────────────────────────────

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

        // ── Request handling ───────────────────────────────────────────────────

        private void ProcessRequest(UpdateEntityAttributeRequest req)
        {
            // 1. Resolve the entity from the network ID.
            if (!_entityMap.TryGetEntity(req.EntityId, out var entity))
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                    "[UpdAttrReq] Entity {0} not found. RequestId={1}",
                    req.EntityId, req.RequestId);
                WriteAck(req.RequestId, SstErrorCode.EntityNotFound);
                return;
            }

            // 2. Validate that a geo-position patch can be honoured.
            if (req.AttributeId == EntityAttribute.eaGeoPosition && _geoTransform == null)
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                    "[UpdAttrReq] eaGeoPosition patch for Entity {0} rejected: no geoTransform. RequestId={1}",
                    req.EntityId, req.RequestId);
                WriteAck(req.RequestId, SstErrorCode.NotSupported);
                return;
            }

            // 3. Compile the attribute patch into updated ECS components by reading the live
            //    ECS state and merging the single attribute change into it.
            List<object> updatedComponents = EntityAttributeCompiler.CompileFromWorld(
                new[] { req.Payload },
                World,
                entity,
                _geoTransform);

            // 4. Write each compiled component back to the ECS entity.
            foreach (var comp in updatedComponents)
                EntityComponentReflector.SetComponent(World, entity, comp);

            // 5. Trigger egress for name changes.  The EntityInfo egress translator watches for
            //    the SmartEgress dirty flag and republishes the dtEntityInfo descriptor immediately.
            //    GeoSpatial egress uses shadow-component comparison and does NOT need a manual hint.
            if (req.AttributeId == EntityAttribute.eaName)
                SmartEgressUtil.MarkDirty(World, entity, EntityInfoOrdinal);

            FdpLog<UpdateEntityAttributeRequestSystem>.Info(
                "[UpdAttrReq] Applied {0} patch for Entity {1}. RequestId={2}",
                req.AttributeId, req.EntityId, req.RequestId);

            WriteAck(req.RequestId, SstErrorCode.Success);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void WriteAck(Guid requestId, SstErrorCode errorCode)
        {
            _ackWriter.Write(new CreateUpdateDeleteEntityAck
            {
                RequestId = requestId,
                ErrorCode = (int)errorCode,
            });
        }
    }
}

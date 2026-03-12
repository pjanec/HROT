using System;
using Bagira.BDC.SSTM;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using FDP.Toolkit.NetworkSpawning;
using FDP.Toolkit.Replication.Services;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Systems
{
    using SstErrorCode = Bagira.BDC.SSTM.SstErrorCode;

    /// <summary>
    /// Consumes <see cref="UpdateEntityAttributeRequest"/> messages from DDS and applies
    /// fine-grained field-level patches to live ECS components via <c>JsonAttributeCompiler</c>.
    ///
    /// <para>
    /// Phase 5 (ATTR-S5T3) wires in the <c>JsonAttributeCompiler</c> and
    /// <c>EcsPatchContext</c>. Until then this system acknowledges every request
    /// without mutating ECS state.
    /// </para>
    ///
    /// <para>
    /// A <see cref="CreateUpdateDeleteEntityAck"/> is always written for every processed sample
    /// so the originating IG can correlate the response.
    /// </para>
    /// </summary>
    public sealed class UpdateEntityAttributeRequestSystem : ComponentSystem
    {
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
            if (!_entityMap.TryGetEntity(req.EntityId, out _))
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                    "[UpdAttrReq] Entity {0} not found. RequestId={1}",
                    req.EntityId, req.RequestId);
                WriteAck(req.RequestId, SstErrorCode.EntityNotFound);
                return;
            }

            // TODO ATTR-S5T3: Wire up JsonAttributeCompiler + EcsPatchContext here (Phase 5).
            // The AttributePatchJson field carries a hierarchical JSON patch such as
            //   {"Name":"Bravo-2"} or {"GeoPosition":{"Latitude":10.0,"Longitude":20.0}}.
            // For now the patch is acknowledged without applying ECS mutations.
            FdpLog<UpdateEntityAttributeRequestSystem>.Info(
                "[UpdAttrReq] JSON attribute patching pending Phase 5. EntityId={0}, RequestId={1}",
                req.EntityId, req.RequestId);

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

using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.SimHost.Util;
using FDP.Kernel.Logging;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Events;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Handles <see cref="CreateEntityRequest"/> messages arriving over DDS.
    /// Translates each valid request into a <see cref="SpawnEntityCommand"/> on the world bus
    /// and immediately sends a <see cref="CreateEntityAck"/> to the requester.
    ///
    /// Design constraint: this system is a thin translator.
    /// It must NOT call CreateEntity, ApplyTo, or BeginConstruction directly —
    /// all ECS spawning is delegated to NetworkSpawningSystem via the event bus.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class CreateEntityRequestSystem : IModuleSystem
    {
        private readonly ICreateEntityRequestSource _requestSource;
        private readonly ICreateEntityAckSink       _ackSink;
        private readonly ITkbDatabase               _tkbDb;
        private readonly INetworkIdAllocator        _idAllocator;
        private readonly IGeographicTransform?      _geoTransform;
        private readonly int                        _localNodeId;

        /// <param name="requestSource">Source of incoming CreateEntityRequest messages (DDS-backed or stub).</param>
        /// <param name="ackSink">Sink for CreateEntityAck responses (DDS-backed or stub).</param>
        /// <param name="tkbDb">TKB database used to validate that the requested entity type exists.</param>
        /// <param name="idAllocator">Allocator that produces unique network entity IDs.</param>
        /// <param name="localNodeId">This node's ID used as <c>OwnerNodeId</c> in SpawnEntityCommand.</param>
        /// <param name="geoTransform">
        /// Optional geographic transform for converting WGS84 GeoSpatial positions to local Cartesian.
        /// When <c>null</c>, GeoSpatial descriptors are included without a VehicleState override.
        /// </param>
        public CreateEntityRequestSystem(
            ICreateEntityRequestSource requestSource,
            ICreateEntityAckSink       ackSink,
            ITkbDatabase               tkbDb,
            INetworkIdAllocator        idAllocator,
            int                        localNodeId,
            IGeographicTransform?      geoTransform = null)
        {
            _requestSource = requestSource ?? throw new ArgumentNullException(nameof(requestSource));
            _ackSink       = ackSink       ?? throw new ArgumentNullException(nameof(ackSink));
            _tkbDb         = tkbDb         ?? throw new ArgumentNullException(nameof(tkbDb));
            _idAllocator   = idAllocator   ?? throw new ArgumentNullException(nameof(idAllocator));
            _localNodeId   = localNodeId;
            _geoTransform  = geoTransform;
        }

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            var requests = _requestSource.TakeRequests();
            foreach (var request in requests)
                ProcessRequest(view, request);
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private void ProcessRequest(ISimulationView view, CreateEntityRequest request)
        {
            try
            {
                // 1. Extract TkbType from descriptors
                long tkbType = DescriptorMapper.ExtractTkbType(request.InitialDescriptors);
                if (tkbType == 0)
                {
                    FdpLog<CreateEntityRequestSystem>.Warn(
                        $"[SimHost] CreateEntity {request.RequestId}: No EntityMaster descriptor or TkbType=0. Rejecting.");
                    SendErrorAck(request.RequestId, errorCode: 400);
                    return;
                }

                // 2. Validate TkbType exists in database
                if (!_tkbDb.TryGetByType(tkbType, out _))
                {
                    FdpLog<CreateEntityRequestSystem>.Warn(
                        $"[SimHost] CreateEntity {request.RequestId}: TkbType={tkbType} not found. Rejecting.");
                    SendErrorAck(request.RequestId, errorCode: 404);
                    return;
                }

                FdpLog<CreateEntityRequestSystem>.Debug(
                    $"[TRACE-SH] Received CreateEntityRequest {request.RequestId} TkbType={tkbType}");

                // 3. Allocate a new network ID
                long newNetworkId = _idAllocator.AllocateId();

                // 4. Map descriptors → ECS component list
                List<object> initialComponents =
                    DescriptorMapper.MapToComponents(request.InitialDescriptors, _geoTransform);

                // 5. Publish SpawnEntityCommand — NetworkSpawningSystem handles all ECS work
                if (view is EntityRepository repo)
                {
                    repo.Bus.PublishManaged(new SpawnEntityCommand
                    {
                        NetworkId         = newNetworkId,
                        TkbType           = tkbType,
                        OwnerNodeId       = _localNodeId,
                        InitType          = ReliableInitType.AllPeers,
                        InitialComponents = initialComponents,
                        RequestId         = request.RequestId,
                    });
                }

                // 6. ACK immediately — entity will be live on the next ECS tick
                _ackSink.WriteAck(new CreateEntityAck
                {
                    RequestId   = request.RequestId,
                    NewEntityId = (int)newNetworkId,
                    ErrorCode   = 0,
                });

                FdpLog<CreateEntityRequestSystem>.Info(
                    $"[SimHost] Spawned entity {newNetworkId} (TkbType={tkbType}) for request {request.RequestId}.");
            }
            catch (Exception ex)
            {
                FdpLog<CreateEntityRequestSystem>.Error(
                    $"[SimHost] CreateEntity failed for request {request.RequestId}: {ex.Message}");
                SendErrorAck(request.RequestId, errorCode: 500);
            }
        }

        private void SendErrorAck(Guid requestId, int errorCode)
        {
            FdpLog<CreateEntityRequestSystem>.Warn(
                $"[TRACE-SH] ERROR: Rejecting Request {requestId} Code={errorCode}");

            _ackSink.WriteAck(new CreateEntityAck
            {
                RequestId   = requestId,
                NewEntityId = 0,
                ErrorCode   = errorCode,
            });
        }
    }
}

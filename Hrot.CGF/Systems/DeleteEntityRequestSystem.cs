using System;
using Hrot.Core.Network;
using FDP.Kernel.Logging;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// Handles entity deletion requests arriving over the network (protocol-neutral).
    ///
    /// <para>
    /// On every <see cref="Execute"/> call the system:
    /// <list type="number">
    ///   <item>Drains the source via a zero-allocation callback.</item>
    ///   <item>Validates the entity exists in <see cref="NetworkEntityMap"/>.</item>
    ///   <item>Sends a Phase-1 <c>InProgress</c> ACK so the ExCon client unblocks immediately.</item>
    ///   <item>Registers the request with <see cref="EntityRequestFinalizationSystem"/> for
    ///     Phase-2 tracking once ELM confirms teardown.</item>
    ///   <item>Publishes a <see cref="DestroyEntityCommand"/> to initiate ELM teardown via
    ///     NetworkSpawningSystem.</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class DeleteEntityRequestSystem : IEcsModuleSystem
    {
        private readonly IEntityDeletionRequestSource _requestSource;
        private readonly IEntityAckSink               _ackSink;
        private readonly NetworkEntityMap             _entityMap;
        private readonly EntityRequestFinalizationSystem _finalizationSystem;
        private readonly int                          _localNodeId;

        public DeleteEntityRequestSystem(
            IEntityDeletionRequestSource requestSource,
            IEntityAckSink               ackSink,
            NetworkEntityMap             entityMap,
            EntityRequestFinalizationSystem finalizationSystem,
            int                          localNodeId = 0)
        {
            _requestSource      = requestSource      ?? throw new ArgumentNullException(nameof(requestSource));
            _ackSink            = ackSink            ?? throw new ArgumentNullException(nameof(ackSink));
            _entityMap          = entityMap          ?? throw new ArgumentNullException(nameof(entityMap));
            _finalizationSystem = finalizationSystem ?? throw new ArgumentNullException(nameof(finalizationSystem));
            _localNodeId        = localNodeId;
        }

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            _requestSource.ProcessRequests(req => ProcessRequest(view, req));
        }

        private void ProcessRequest(ISimulationView view, EntityDeletionRequest request)
        {
            try
            {
                // Validate the entity is currently known to this node.
                if (!_entityMap.TryGetEntity(request.EntityId, out _))
                {
                    FdpLog<DeleteEntityRequestSystem>.Warn(
                        $"[Node-{_localNodeId}] DeleteEntity {request.RequestId}: EntityId={request.EntityId} not found. Rejecting.");
                    _ackSink.WriteAck(request.RequestId, request.EntityId, EntityOperationStatus.EntityNotFound);
                    return;
                }

                // Phase 1: send InProgress ACK — client unblocks immediately.
                _ackSink.WriteAck(request.RequestId, request.EntityId, EntityOperationStatus.InProgress);

                // Register for Phase-2 ACK once ELM confirms the entity is gone.
                _finalizationSystem.Track(request.EntityId, request.RequestId, RequestKind.Delete);

                // Initiate ELM teardown via event bus.
                if (view is EntityRepository repo)
                {
                    repo.Bus.PublishManaged(new DestroyEntityCommand
                    {
                        NetworkId = request.EntityId,
                        Reason    = $"DeleteEntityRequest:{request.RequestId}",
                    });
                }

                FdpLog<DeleteEntityRequestSystem>.Info(
                    $"[Node-{_localNodeId}] DeleteEntity {request.EntityId} accepted " +
                    $"for request {request.RequestId}.");
            }
            catch (Exception ex)
            {
                FdpLog<DeleteEntityRequestSystem>.Error(
                    $"[Node-{_localNodeId}] DeleteEntity failed for request {request.RequestId}: {ex.Message}");
                _ackSink.WriteAck(request.RequestId, request.EntityId, EntityOperationStatus.EntityNotFound);
            }
        }
    }
}


// ⭐⭐ MOVED HERE 2026-08-31 (Q65 obstacle 1) FROM Hrot/Subsystems/Hrot.CGF/Systems/.
//
// WHY: this is the REQUEST TIER of entity genesis, and it is not CGF's. Q65 measured that
// `isDefaultProcessor` is a BROADCAST TIEBREAKER, not an authority gate -- CreateEntityRequestSystem
// processes a request targeted at the LOCAL node regardless of that flag. So every ECS-equipped node
// should register this system; only one node carries isDefaultProcessor: true, for Owner == 0 broadcasts
// from non-ECS clients like ExCon.
//
// While it lived in the Hrot.CGF host assembly, "every node registers it" was not even EXPRESSIBLE:
// only CGF, the Editor and the Stride editor could construct it. This move is what makes
// EntityCreationPack (DESIGN step 3) possible, and it is a PURE MOVE -- no behaviour change.
//
// The namespace changed from Hrot.CGF.Systems to Hrot.Common.Systems deliberately: keeping "CGF" in
// the name of a type every node uses would perpetuate exactly the misconception Q65 exists to kill.
//
// WHY Hrot.Common AND NOT Hrot.Core (Q65 section 5.4 said Hrot.Core, and that was WRONG):
// CreateEntityRequestSystem constructs Hrot.Common.Serializers.InitialUnitSubordinateIntent by
// FULLY-QUALIFIED name in its child-blueprint branch. Hrot.Common already references Hrot.Core, so
// Hrot.Core -> Hrot.Common would be a CYCLE. Measured 2026-08-31. Hrot.Common is reachable from
// every host (CGF, SimHost, IG, Stride, ClusterRunner directly; Hrot.Editor transitively via
// SimHost/CGF/NED) and is where SharedApplicationBootstrapper already lives, so "every node can
// register it" holds.
//
// 📄 docs/blueprints/Architect_Question_65_Entity_Genesis_Uniformity.md sections 5.4 and 6
// 📄 docs/DESIGN_Entity_Creation_Unification.md section 3.4
using System;
using Hrot.Core.Network;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Systems
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


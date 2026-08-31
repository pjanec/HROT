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
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Systems
{
    /// <summary>
    /// Phase ordering for Two-ACK lifecycle requests.
    /// </summary>
    internal enum RequestKind { Create, Delete }

    /// <summary>
    /// Monitors in-flight entity creation/deletion requests and dispatches
    /// Phase-2 (final) ACK messages once the ECS lifecycle confirms the outcome.
    ///
    /// <para>
    /// Phase 1 (InProgress) is sent immediately by the originating request system.
    /// Phase 2 is sent here in PostSimulation once:
    /// <list type="bullet">
    ///   <item><b>Create:</b> entity appears in the <see cref="NetworkEntityMap"/> and
    ///     reaches <see cref="EntityLifecycle.Active"/>.</item>
    ///   <item><b>Delete:</b> entity is no longer alive or no longer registered in the map.</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class EntityRequestFinalizationSystem : IEcsModuleSystem
    {
        private struct PendingRequest
        {
            public Guid        RequestId;
            public RequestKind Kind;
        }

        private readonly Dictionary<long, PendingRequest> _tracked   = new();
        private readonly IEntityAckSink                    _ackSink;
        private readonly NetworkEntityMap                  _entityMap;

        public EntityRequestFinalizationSystem(
            IEntityAckSink   ackSink,
            NetworkEntityMap entityMap)
        {
            _ackSink   = ackSink   ?? throw new ArgumentNullException(nameof(ackSink));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <summary>
        /// Registers a request for Phase-2 tracking.
        /// Called by <see cref="CreateEntityRequestSystem"/> or <see cref="DeleteEntityRequestSystem"/>
        /// immediately after dispatching the Phase-1 InProgress ACK.
        /// </summary>
        internal void Track(long networkId, Guid requestId, RequestKind kind)
        {
            _tracked[networkId] = new PendingRequest { RequestId = requestId, Kind = kind };
        }

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_tracked.Count == 0)
                return;

            // Collect IDs resolved this frame to avoid mutating the dictionary mid-iteration.
            var toRemove = new List<long>(_tracked.Count);

            foreach (var kvp in _tracked)
            {
                long networkId   = kvp.Key;
                var  pending     = kvp.Value;
                bool resolved    = false;
                var  finalStatus = EntityOperationStatus.EntityNotFound;

                if (pending.Kind == RequestKind.Create)
                {
                    if (_entityMap.TryGetEntity(networkId, out var entity))
                    {
                        if (!view.IsAlive(entity))
                        {
                            // Entity was registered but died before completing construction.
                            resolved    = true;
                            finalStatus = EntityOperationStatus.EntityNotFound;
                        }
                        else if (view is EntityRepository repo
                              && repo.GetLifecycleState(entity) == EntityLifecycle.Active)
                        {
                            // Entity reached Active — distributed handshake complete.
                            resolved    = true;
                            finalStatus = EntityOperationStatus.Success;
                        }
                        // else: entity is alive but still Constructing — keep waiting.
                    }
                    // else: entity not yet registered in map — keep waiting (NetworkSpawningSystem
                    // processes SpawnEntityCommand on the next frame after the write-buffer swap).
                }
                else // RequestKind.Delete
                {
                    if (!_entityMap.TryGetEntity(networkId, out var entity) || !view.IsAlive(entity))
                    {
                        // Entity is gone — teardown confirmed.
                        resolved    = true;
                        finalStatus = EntityOperationStatus.Success;
                    }
                    // else: entity still alive — wait for ELM to finish teardown.
                }

                if (resolved)
                {
                    _ackSink.WriteAck(pending.RequestId, networkId, finalStatus);
                    toRemove.Add(networkId);
                }
            }

            foreach (var id in toRemove)
                _tracked.Remove(id);
        }
    }
}


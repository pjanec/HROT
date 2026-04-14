using System;
using System.Collections.Generic;
using Hrot.Core.Network;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.CGF.Systems
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


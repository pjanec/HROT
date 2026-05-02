using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Map.Common.Replication;
using Hrot.NED.Descriptors;
using System;
using System.Collections.Generic;

namespace Hrot.Map.Common.Replication.Ingress
{
	/// <summary>
	/// Ingress translator for the Hrot <c>EntityInfo</c> DDS topic.
	///
	/// The ECS <see cref="Fdp.Core.EntityInfo"/> is an unmanaged struct applied directly
	/// via <see cref="EntityRepository.SetComponent{T}"/> when the view exposes a writable
	/// <see cref="EntityRepository"/> (the normal IG role).  When the repo is unavailable the
	/// translator falls back to publishing an <see cref="UpdateEntityCommand"/> so that a
	/// <c>NetworkSpawningSystem</c> can apply the component instead.
	///
	/// Commander assignment is deferred via <c>_pendingSubordinates</c> and
	/// <c>_pendingUnspawnedSubordinates</c> when either the commander or the subordinate
	/// entity has not yet appeared in <see cref="NetworkEntityMap"/>.  Deferral queues are
	/// drained whenever <see cref="NetworkEntityMap.EntityRegistered"/> fires.
	///
	/// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
	/// </summary>
	public class EntityInfoIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityInfo";
        private const long OrdinalValue = (long)EDescriptorType.dtEntityInfo; 

        private readonly DdsReader<Hrot.NED.Descriptors.EntityInfo>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly FdpEventBus _eventBus;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        // Keyed by commander net ID. Value = list of subordinate entities waiting for that commander.
        private readonly Dictionary<long, List<(Entity Subordinate, TacticalDesignation Designation)>> _pendingSubordinates = new();

        // Keyed by subordinate net ID. Used when the subordinate itself has not yet spawned.
        private readonly Dictionary<long, (long CommanderNetId, TacticalDesignation Designation)> _pendingUnspawnedSubordinates = new();

        // Fired by NetworkEntityMap when a new entity is registered. Used to drain both pending queues.
        private readonly List<long> _recentlyRegistered = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityInfoIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            FdpEventBus eventBus,
            GhostCreationSystem ghostCreationSystem,
            long localNodeId)
        {
			// participant may be null in unit-test mode — PollIngress becomes a no-op
			_reader = participant is not null ? new DdsReader<Hrot.NED.Descriptors.EntityInfo>( participant ) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = localNodeId;
            _entityMap.EntityRegistered += OnEntityRegistered;
        }

        private void OnEntityRegistered(long netId, Entity entity)
        {
            _recentlyRegistered.Add(netId);
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid) continue;

                    if (sample.Info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive) continue;

                    ReceivedSampleCount++;
                    var info   = sample.Data;
                    long netId = info.EntityId;
                    var repo   = view as EntityRepository;

                    if (!_entityMap.TryGetEntity(netId, out _))
                    {
                        if (repo == null)
                        {
                            FdpLog<EntityInfoIngressTranslator>.Warn(
                                "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                            continue;
                        }
                        _ghostCreationSystem.CreateGhost(repo, netId);
                    }

                    ProcessSample(info, netId, repo);
                }
            }

            // Drain recently registered entities — resolve any pending queues.
            // This runs regardless of whether DDS reads occurred so that test-mode
            // callers can trigger deferred resolution by calling PollIngress after
            // registering a new entity via NetworkEntityMap.Register.
            foreach (var registeredId in _recentlyRegistered)
                DrainPendingForRegistered(registeredId, view as EntityRepository);
            _recentlyRegistered.Clear();
        }

        // ── Egress (ingress-only translator — nothing to publish) ────────────

        public void ScanAndPublish(ISimulationView view) { }

        // ── Ghost promotion helper ────────────────────────────────────────────

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is Hrot.NED.Descriptors.EntityInfo info)
            {
                repo.SetComponent(entity, new Fdp.Core.EntityInfo
                {
                    Name    = info.Name,
                    ForceId = (ForceId)(int)info.ForceIdentifier,
                });
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Dispose(long networkEntityId)
        {
            // Remove as a pending commander.
            _pendingSubordinates.Remove(networkEntityId);

            // Remove as a pending un-spawned subordinate.
            _pendingUnspawnedSubordinates.Remove(networkEntityId);

            // Remove as a spawned-but-deferred subordinate in any commander's list.
            if (_entityMap.TryGetEntity(networkEntityId, out var subEnt))
            {
                foreach (var list in _pendingSubordinates.Values)
                    list.RemoveAll(e => e.Subordinate.Equals(subEnt));
            }
        }

        /// <summary>Unsubscribes from <see cref="NetworkEntityMap.EntityRegistered"/>.</summary>
        internal void Shutdown() => _entityMap.EntityRegistered -= OnEntityRegistered;

        internal void ProcessSample(Hrot.NED.Descriptors.EntityInfo info, long netId, EntityRepository? repo = null)
        {
            var igData = new Fdp.Core.EntityInfo
            {
                Name    = info.Name,
                ForceId = (ForceId)(int)info.ForceIdentifier,
            };

            bool hasAuthority = false;

            if (repo != null && _entityMap.TryGetEntity(netId, out var entity))
            {
                repo.SetComponent(entity, igData);

                // Check if the local node owns the EntityInfo descriptor
                long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
                hasAuthority = repo.HasAuthority(entity, packedKey);
            }
            else
            {
                _eventBus.PublishManaged(new UpdateEntityCommand
                {
                    NetworkId          = netId,
                    ComponentsToUpdate = new List<object> { igData },
                    RequestId          = Guid.Empty,
                });
            }

            // Loopback Prevention
            // If this node owns the EntityInfo descriptor, it is the absolute source of truth.
            // We must drop incoming hierarchy payloads to prevent loopback packets 
            // from destroying the local ECS hierarchy.
            if (hasAuthority)
                return;


            // Commander assignment / removal.
            long commanderNetId = info.CommanderId;
            var designation     = TacticalDesignationMapper.ToEcs(info.TacticalDesignation);

            if (commanderNetId == 0)
            {
                // Only publish CmdRemoveSubordinate if the entity currently has a UnitSubordinate.
                if (repo != null && _entityMap.TryGetEntity(netId, out var subEntity)
                    && repo.HasComponent<UnitSubordinate>(subEntity))
                {
                    _eventBus.Publish(new CmdRemoveSubordinate { Subordinate = subEntity });
                }
                return;
            }

            // Scrub this subordinate from all existing pending queues before re-queuing.
            RemoveFromAllPendingQueues(netId);

            // Case 1: subordinate entity is not yet spawned.
            if (!_entityMap.TryGetEntity(netId, out _))
            {
                _pendingUnspawnedSubordinates[netId] = (commanderNetId, designation);
                return;
            }

            // Case 2: subordinate is alive, commander is also alive.
            if (_entityMap.TryGetEntity(commanderNetId, out var cmdEntity)
                && _entityMap.TryGetEntity(netId, out var subEntity2))
            {
                _eventBus.Publish(new CmdAssignSubordinate
                {
                    Subordinate = subEntity2,
                    Commander   = cmdEntity,
                    Designation = designation,
                });
                return;
            }

            // Case 3: subordinate is alive, commander is not yet spawned — defer by commander.
            if (!_pendingSubordinates.TryGetValue(commanderNetId, out var list))
            {
                list = new List<(Entity, TacticalDesignation)>();
                _pendingSubordinates[commanderNetId] = list;
            }
            if (_entityMap.TryGetEntity(netId, out var subEntity3))
                list.Add((subEntity3, designation));
        }

        private void DrainPendingForRegistered(long registeredNetId, EntityRepository? repo)
        {
            // 1. If a previously un-spawned subordinate just appeared:
            if (_pendingUnspawnedSubordinates.TryGetValue(registeredNetId, out var pending))
            {
                _pendingUnspawnedSubordinates.Remove(registeredNetId);
                if (!_entityMap.TryGetEntity(registeredNetId, out var subEntity)) return;

                if (_entityMap.TryGetEntity(pending.CommanderNetId, out var cmdEntity))
                {
                    // Commander already alive — publish immediately.
                    _eventBus.Publish(new CmdAssignSubordinate
                    {
                        Subordinate = subEntity,
                        Commander   = cmdEntity,
                        Designation = pending.Designation,
                    });
                }
                else
                {
                    // Commander not yet alive — move to deferred-by-commander queue.
                    if (!_pendingSubordinates.TryGetValue(pending.CommanderNetId, out var list))
                    {
                        list = new List<(Entity, TacticalDesignation)>();
                        _pendingSubordinates[pending.CommanderNetId] = list;
                    }
                    list.Add((subEntity, pending.Designation));
                }
                return;
            }

            // 2. If a commander just appeared — resolve all its waiting subordinates:
            if (!_pendingSubordinates.TryGetValue(registeredNetId, out var subs)) return;
            if (!_entityMap.TryGetEntity(registeredNetId, out var cmdEnt)) return;

            foreach (var (sub, desig) in subs)
            {
                _eventBus.Publish(new CmdAssignSubordinate
                {
                    Subordinate = sub,
                    Commander   = cmdEnt,
                    Designation = desig,
                });
            }
            _pendingSubordinates.Remove(registeredNetId);
        }

        private void RemoveFromAllPendingQueues(long subordinateNetId)
        {
            _pendingUnspawnedSubordinates.Remove(subordinateNetId);

            foreach (var list in _pendingSubordinates.Values)
            {
                if (_entityMap.TryGetEntity(subordinateNetId, out var subEnt))
                    list.RemoveAll(e => e.Subordinate.Equals(subEnt));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Core.Logging;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

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
	/// Entities not yet registered in the map are silently skipped.
	/// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
	/// </summary>
	public class EntityInfoIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityInfo";
        private const long OrdinalValue = 20;

        private readonly DdsReader<Hrot.NED.Descriptors.EntityInfo>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly FdpEventBus _eventBus;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

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
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return; // test mode — no DDS participant supplied
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                if (sample.Info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                    continue;

                ReceivedSampleCount++;
                var info = sample.Data;
                long netId = info.EntityId;
                var repo = view as EntityRepository;

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

        // ── Egress (ingress-only translator — nothing to publish) ────────────

        public void ScanAndPublish(ISimulationView view) { }

        // ── Ghost promotion helper ────────────────────────────────────────────

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if ( data is Hrot.NED.Descriptors.EntityInfo info)
            {
                repo.SetComponent( entity, new Fdp.Core.EntityInfo
                {
                    Name = info.Name,
                    ForceId = (ForceId)(int)info.ForceIdentifier,
                    CommanderId = info.CommanderId
                });
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Dispose(long networkEntityId) { /* IG does not write Hrot.NED.Descriptors.EntityInfo */ }

        internal void ProcessSample(Hrot.NED.Descriptors.EntityInfo info, long netId, EntityRepository? repo = null)
        {
            var igData = new Fdp.Core.EntityInfo
            {
                Name = info.Name,
                ForceId = (ForceId)(int)info.ForceIdentifier,
                CommanderId = info.CommanderId
            };

            // Apply directly via the repo when available (IG role has no NetworkSpawningSystem
            // to consume UpdateEntityCommand, so event-bus routing would silently drop the update).
            if (repo != null && _entityMap.TryGetEntity(netId, out var entity))
            {
                repo.SetComponent(entity, igData);
                return;
            }

            _eventBus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId = netId,
                ComponentsToUpdate = new List<object> { igData },
                RequestId = Guid.Empty,
            });
        }
    }
}

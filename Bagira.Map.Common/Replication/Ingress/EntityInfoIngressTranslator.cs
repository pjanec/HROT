using System;
using System.Collections.Generic;
using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Kernel.Logging;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Replication.Ingress
{
	/// <summary>
	/// Ingress translator for the Bagira <c>EntityInfo</c> DDS topic.
	///
	/// The ECS <see cref="IG.Components.EntityInfo"/> is now an unmanaged struct, so it can
	/// be applied directly via <see cref="EntityRepository.SetComponent{T}"/>.
	/// For the update path the translator publishes an
	/// <see cref="UpdateEntityCommand"/> onto the <see cref="FdpEventBus"/>; the
	/// <c>NetworkSpawningSystem</c> applies the component through
	/// <c>EntityComponentReflector</c>.
	///
	/// Entities not yet registered in the map are silently skipped.
	/// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
	/// </summary>
	public class EntityInfoIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityInfo";
        private const long OrdinalValue = 20;

        private readonly DdsReader<BDC.SSTD.EntityInfo>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly FdpEventBus _eventBus;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        public EntityInfoIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            FdpEventBus eventBus,
            GhostCreationSystem ghostCreationSystem)
        {
			// participant may be null in unit-test mode — PollIngress becomes a no-op
			_reader = participant is not null ? new DdsReader<BDC.SSTD.EntityInfo>( participant ) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
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

                var info = sample.Data;
                long netId = info.EntityId;

                if (!_entityMap.TryGetEntity(netId, out _))
                {
                    var repo = view as EntityRepository;
                    if (repo == null)
                    {
                        FdpLog<EntityInfoIngressTranslator>.Warn(
                            "[IG] Cannot create ghost for NetID {0}: view is read-only.", netId);
                        continue;
                    }

                    _ghostCreationSystem.CreateGhost(repo, netId);
                }

                ProcessSample(info, netId);
            }
        }

        // ── Egress (ingress-only translator — nothing to publish) ────────────

        public void ScanAndPublish(ISimulationView view) { }

        // ── Ghost promotion helper ────────────────────────────────────────────

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if ( data is BDC.SSTD.EntityInfo info)
            {
                repo.SetComponent( entity, new IG.Components.EntityInfo
                {
                    Name = info.Name,
                    ForceId = (ForceId)(int)info.ForceIdentifier,
                    CommanderId = info.CommanderId
                });
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Dispose(long networkEntityId) { /* IG does not write EntityInfo */ }

        internal void ProcessSample( BDC.SSTD.EntityInfo info, long netId)
        {
            var igData = new IG.Components.EntityInfo
            {
                Name = info.Name,
                ForceId = (ForceId)(int)info.ForceIdentifier,
                CommanderId = info.CommanderId
            };

            _eventBus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId = netId,
                ComponentsToUpdate = new List<object> { igData },
                RequestId = Guid.Empty,
            });
        }
    }
}

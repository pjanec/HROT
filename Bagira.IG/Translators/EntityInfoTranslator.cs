using System;
using System.Collections.Generic;
using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Translators
{
    /// <summary>
    /// Ingress translator for the Bagira <c>EntityInfo</c> DDS topic.
    ///
    /// <see cref="EntityInfo"/> contains managed fields (e.g. <c>string Name</c>) and
    /// cannot be applied via <see cref="IEntityCommandBuffer.SetComponent{T}"/> which
    /// requires <c>T : unmanaged</c>.  Instead, the translator publishes an
    /// <see cref="UpdateEntityCommand"/> onto the <see cref="FdpEventBus"/>; the
    /// <c>NetworkSpawningSystem</c> applies the component through
    /// <c>EntityComponentReflector</c>, which handles managed struct types correctly.
    ///
    /// Entities not yet registered in the map are silently skipped.
    /// IG is a ghost-only node — <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class EntityInfoTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityInfo";
        private const long   OrdinalValue = 20;

        private readonly DdsReader<EntityInfo>? _reader;
        private readonly NetworkEntityMap        _entityMap;
        private readonly FdpEventBus             _eventBus;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => OrdinalValue;

        public EntityInfoTranslator(
            DdsParticipant?  participant,
            NetworkEntityMap entityMap,
            FdpEventBus      eventBus)
        {
            // participant may be null in unit-test mode — PollIngress becomes a no-op
            _reader    = participant is not null ? new DdsReader<EntityInfo>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _eventBus  = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
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

                var info   = sample.Data;
                long netId = info.EntityId;

                if (!_entityMap.TryGetEntity(netId, out _))
                    continue; // Not spawned yet — retry next tick

                ProcessSample(info, netId);
            }
        }

        // ── Egress (IG is ghost-only — nothing to publish) ───────────────────

        public void ScanAndPublish(ISimulationView view) { }

        // ── Ghost promotion helper ────────────────────────────────────────────

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityInfo info)
            {
                repo.SetManagedComponent(entity, new IgEntityData
                {
                    Name = info.Name,
                    ForceId = (ForceId)(int)info.ForceIdentifier,
                    CommanderId = info.CommanderId
                });
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Dispose(long networkEntityId) { /* IG does not write EntityInfo */ }

        internal void ProcessSample(EntityInfo info, long netId)
        {
            var igData = new IgEntityData
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

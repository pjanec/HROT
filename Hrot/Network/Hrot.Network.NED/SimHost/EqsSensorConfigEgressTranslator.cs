using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Brain-side egress translator: publishes <see cref="EqsSensorConfigTopic"/> to the Muscle
    /// node whenever an <c>EqsSensor</c> component is added or mutated on an authority-owned entity.
    /// Uses <c>SmartEgressUtil</c> for dirty-tracking so the topic is only sent on actual changes.
    /// </summary>
    public sealed class EqsSensorConfigEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsSensorConfig";
        private readonly DdsWriter<EqsSensorConfigTopic>? _writer;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsSensorConfig;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EqsSensorConfigEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _writer = new DdsWriter<EqsSensorConfigTopic>(participant, DdsTopicName);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            var query = view.Query()
                .With<EqsSensor>()
                .With<NetworkIdentity>()
                .Build();

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;

                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
                    continue;

                ref readonly var sensor = ref view.GetComponentRO<EqsSensor>(entity);
                ref readonly var netId  = ref view.GetComponentRO<NetworkIdentity>(entity);

                _writer.Write(new EqsSensorConfigTopic
                {
                    EntityId        = netId.Value,
                    BlueprintId     = sensor.BlueprintId,
                    Epoch           = sensor.Epoch,
                    SearchRadius    = sensor.SearchRadius,
                    FactionFilter   = sensor.FactionFilter,
                    ThreatThreshold = sensor.ThreatThreshold,
                    PublishPolicy   = sensor.PublishPolicy,
                    Priority        = sensor.Priority,
                });

                SentSampleCount++;
                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
            }

            // Removal detection: find entities that were previously published for this
            // descriptor but no longer carry EqsSensor. Emit NOT_ALIVE_DISPOSED so the
            // Muscle-side ingress translator removes the component from the ghost entity.
            var removalQuery = view.Query()
                .With<NetworkIdentity>()
                .Without<EqsSensor>()
                .Build();

            foreach (var entity in removalQuery)
            {
                if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;
                if (!view.HasManagedComponent<EgressPublicationState>(entity)) continue;

                var state = view.GetManagedComponentRO<EgressPublicationState>(entity);
                if (!state.LastPublishedTickMap.ContainsKey(DescriptorOrdinal)) continue;

                // Entity lost EqsSensor after a prior publish -- send dispose.
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                _writer.DisposeInstance(new EqsSensorConfigTopic { EntityId = netId.Value });
                state.LastPublishedTickMap.Remove(DescriptorOrdinal);
            }
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId)
        {
            _writer?.DisposeInstance(new EqsSensorConfigTopic { EntityId = networkEntityId });
        }
    }
}


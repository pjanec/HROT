using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Muscle-side ingress translator: receives <see cref="EqsSensorConfigTopic"/> samples
    /// and applies the <c>EqsSensor</c> component to the corresponding ghost entity so the
    /// solver picks it up on the next tick.
    /// On <c>NOT_ALIVE_DISPOSED</c>, removes <c>EqsSensor</c> from the ghost entity,
    /// signalling the solver to drop the query.
    /// </summary>
    public sealed class EqsSensorConfigIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsSensorConfig";
        private readonly DdsReader<EqsSensorConfigTopic>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsSensorConfig;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EqsSensorConfigIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
            _entityMap = entityMap;
            _reader = participant != null
                ? new DdsReader<EqsSensorConfigTopic>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                long entityId;
                if (sample.IsValid)
                {
                    entityId = sample.Data.EntityId;
                    ReceivedSampleCount++;
                }
                else
                {
                    // For NOT_ALIVE samples the managed .Data property throws.
                    // Read key fields directly from the native serialised buffer.
                    var keyData = DdsTypeSupport.FromNative<EqsSensorConfigTopic>(sample.NativePtr);
                    entityId = keyData.EntityId;
                }

                if (!_entityMap.TryGetEntity(entityId, out var entity)) continue;

                if (sample.IsValid)
                {
                    cmd.SetComponent(entity, new EqsSensor
                    {
                        BlueprintId     = sample.Data.BlueprintId,
                        Epoch           = sample.Data.Epoch,
                        SearchRadius    = sample.Data.SearchRadius,
                        FactionFilter   = sample.Data.FactionFilter,
                        ThreatThreshold = sample.Data.ThreatThreshold,
                        PublishPolicy   = sample.Data.PublishPolicy,
                        Priority        = sample.Data.Priority,
                    });
                }
                else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                {
                    cmd.RemoveComponent<EqsSensor>(entity);
                }
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}


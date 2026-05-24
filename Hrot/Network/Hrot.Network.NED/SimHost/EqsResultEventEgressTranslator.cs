using System;
using System.Collections.Generic;
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
    /// Muscle-side egress translator: reads <see cref="EqsResultEvent"/>s from the local bus,
    /// dereferences the <see cref="EqsResultPool"/> handle to build a
    /// <see cref="List{EqsResultEntry}"/> payload, and publishes <see cref="EqsResultTopic"/>
    /// to the Brain node via DDS.
    /// </summary>
    public sealed class EqsResultEventEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsResult";
        private readonly DdsWriter<EqsResultTopic>? _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EqsResultEventEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
            _writer    = new DdsWriter<EqsResultTopic>(participant, DdsTopicName);
            _entityMap = entityMap;
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            if (view is not EntityRepository repo) return;

            var events = view.ReadEvents<EqsResultEvent>();
            if (events.IsEmpty) return;

            for (int ei = 0; ei < events.Length; ei++)
            {
                ref readonly var evt = ref events[ei];

                // Build the managed DDS payload from the unmanaged pool slice.
                // EntryCount == 0 (Phase 1 stub) is valid: publish an empty result.
                var entries = new List<EqsResultEntry>(evt.EntryCount);
                if (evt.EntryCount > 0)
                {
                    if (!repo.HasSingletonUnmanaged<EqsResultPool>()) continue;
                    ref readonly var pool = ref repo.GetSingletonUnmanaged<EqsResultPool>();

                    for (int i = 0; i < evt.EntryCount; i++)
                    {
                        ref readonly var r = ref pool.Results[evt.ResultHandle + i];

                        // For entity-shaped results translate local EntityId -> NetworkId.
                        // EntityId = 0 means positional candidate (no translation needed).
                        long resolvedNetId = 0L;
                        if (r.EntityId != 0L && r.EntityId != -1L)
                        {
                            var targetEntity = new Entity((ulong)r.EntityId);
                            _entityMap.TryGetNetworkId(targetEntity, out resolvedNetId);
                        }

                        entries.Add(new EqsResultEntry
                        {
                            EntityId  = resolvedNetId,
                            PositionX = r.PositionX,
                            PositionY = r.PositionY,
                            Score     = r.Score,
                            Flags     = (ushort)r.Flags,
                        });
                    }
                }

                _writer.Write(new EqsResultTopic
                {
                    SensorNetworkId = evt.SensorNetworkId,
                    Epoch           = evt.Epoch,
                    RefreshTick     = evt.RefreshTick,
                    Results         = entries,
                });

                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}


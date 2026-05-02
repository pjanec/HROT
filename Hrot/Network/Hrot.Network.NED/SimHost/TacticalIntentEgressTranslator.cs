using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Commander Brain egress translator: reads <see cref="AssignTacticalIntentEvent"/>
    /// managed events from the local bus and writes a <see cref="TacticalIntentRequest"/>
    /// DDS message for each event whose target entity is NOT owned by the local Brain node.
    ///
    /// <para>
    /// <b>Authority gate:</b> Only publishes when
    /// <c>!repo.HasAuthority&lt;BehaviorState&gt;(evt.Entity)</c>.
    /// Locally-owned entities are handled by <c>TacticalIntentResolutionSystem</c>
    /// in the same frame; no DDS traffic needed.
    /// </para>
    /// </summary>
    public sealed class TacticalIntentEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "TacticalIntentRequest";

        private readonly IDdsWriter<TacticalIntentRequest> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtTacticalIntentRequest;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public TacticalIntentEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<TacticalIntentRequest>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Internal test constructor — accepts a stub writer.</summary>
        internal TacticalIntentEgressTranslator(
            IDdsWriter<TacticalIntentRequest> writer,
            NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (view is not EntityRepository repo) return;

            var events = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();

            foreach (var evt in events)
            {
                if (evt is null) continue;

                // Authority gate: locally-owned entities are resolved by
                // TacticalIntentResolutionSystem in the same frame.
                if (repo.HasAuthority<BehaviorState>(evt.Entity)) continue;

                if (!_entityMap.TryGetNetworkId(evt.Entity, out long networkId))
                {
                    FdpLog<TacticalIntentEgressTranslator>.Warn(
                        "[TacticalIntentEgress] Entity #{0} not in NetworkEntityMap — skipping intent.",
                        evt.Entity.Index);
                    continue;
                }

                _writer.Write(new TacticalIntentRequest
                {
                    TargetEntityId = networkId,
                    IntentId       = evt.IntentId,
                    JsonParams     = evt.JsonParams,
                });
                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}

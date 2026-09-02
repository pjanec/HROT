using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that converts <see cref="SpawnEntityCommand"/> events consumed from
    /// <see cref="FdpEventBus"/> into <see cref="CreateEntityRequest"/> DDS samples.
    ///
    /// <para>
    /// All commands follow the standard path: command fields (including
    /// <see cref="SpawnEntityCommand.InitialComponents"/>) are serialised into a new
    /// <see cref="CreateEntityRequest"/> containing <c>dtEntityMaster</c>,
    /// <c>dtWorldPos</c>, and — when geometry components are present —
    /// <c>dtMapVisualOverlay</c> or <c>dtMapRoute</c> descriptors.
    /// </para>
    /// </summary>
    public class SpawnEntityCommandEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "CreateEntityRequest";

        // Synthetic ordinal — this translator is event-driven (PollIngress only); ScanAndPublish is empty.
        private const long OrdinalValue = -1001L;

        private readonly IDdsWriter<CreateEntityRequest> _writer;
        private readonly FdpEventBus _eventBus;
        private readonly IGeographicTransform? _geoTransform;
        private readonly long _localNodeId;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public SpawnEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus,
            IGeographicTransform? geoTransform,
            long localNodeId = 0)
            : this(new DdsWriterAdapter<CreateEntityRequest>(participant, DdsTopicName), eventBus, geoTransform, localNodeId)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal SpawnEntityCommandEgressTranslator(
            IDdsWriter<CreateEntityRequest> writer,
            FdpEventBus eventBus,
            IGeographicTransform? geoTransform,
            long localNodeId = 0)
        {
            _writer       = writer    ?? throw new ArgumentNullException(nameof(writer));
            _eventBus     = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
            _geoTransform = geoTransform;
            _localNodeId  = localNodeId;
        }

        /// <summary>
        /// Consumes pending <see cref="SpawnEntityCommand"/> events from the event bus and
        /// writes each as a <see cref="CreateEntityRequest"/> to DDS.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            foreach (var spawnCmd in _eventBus.ReadManaged<SpawnEntityCommand>())
            {
                var request = BuildCreateEntityRequest(spawnCmd);
                _writer.Write(request);
                SentSampleCount++;
                FdpLog<SpawnEntityCommandEgressTranslator>.Debug(
                    "[Node-{0}] SpawnCmd \u2192 CreateEntityRequest req={1} tkbType={2}",
                    _localNodeId, request.RequestId, spawnCmd.TkbType);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Adapts the local ORDER to the neutral intent shape and delegates to
        /// <see cref="CreateEntityRequestDescriptorBuilder"/>, which owns the descriptor construction
        /// so the new request-level egress can reuse it verbatim (R-137).
        /// </summary>
        private CreateEntityRequest BuildCreateEntityRequest(SpawnEntityCommand cmd)
            => CreateEntityRequestDescriptorBuilder.Build(
                requestId:             cmd.RequestId,
                tkbType:               cmd.TkbType,
                initialAttributesJson: cmd.InitialAttributesJson,
                anchor:                CreateEntityRequestDescriptorBuilder.ResolveAnchor(
                                           cmd.InitialTransform, cmd.InitialComponents),
                initialComponents:     cmd.InitialComponents,
                geoTransform:          _geoTransform);
    }
}

using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that converts <see cref="DestroyEntityCommand"/> events consumed from
    /// <see cref="FdpEventBus"/> into <see cref="DeleteEntityRequest"/> DDS samples.
    ///
    /// <para>
    /// The distributed IG publishes <see cref="DestroyEntityCommand"/> on the bus when an entity
    /// is deleted via the context menu (or Delete key). This translator intercepts those events
    /// and forwards them to the SimHost (authority) via DDS.
    /// </para>
    /// </summary>
    public class DestroyEntityCommandEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "DeleteEntityRequest";

        // Synthetic ordinal for event-driven translator (PollIngress only).
        private const long OrdinalValue = -1003L;

        private readonly IDdsWriter<DeleteEntityRequest> _writer;
        private readonly FdpEventBus _eventBus;
        private readonly long _localNodeId;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public DestroyEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus,
            long localNodeId = 0)
            : this(new DdsWriterAdapter<DeleteEntityRequest>(participant, DdsTopicName), eventBus, localNodeId)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal DestroyEntityCommandEgressTranslator(
            IDdsWriter<DeleteEntityRequest> writer,
            FdpEventBus eventBus,
            long localNodeId = 0)
        {
            _writer   = writer    ?? throw new ArgumentNullException(nameof(writer));
            _eventBus = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId = localNodeId;
        }

        /// <summary>
        /// Consumes pending <see cref="DestroyEntityCommand"/> events from the event bus and
        /// writes each as a <see cref="DeleteEntityRequest"/> to DDS.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            foreach (var destroyCmd in _eventBus.ConsumeManaged<DestroyEntityCommand>())
            {
                // Prevent echo loop: do not forward bottom-up disposal notifications back to
                // the server.  When SimHost sends EntityMaster DISPOSE the IG publishes a
                // DestroyEntityCommand with this reason; forwarding it would send a second
                // DeleteEntityRequest for an already-deleted entity (error code 3).
                if (destroyCmd.Reason == "EntityMaster disposed") continue;

                var request = new DeleteEntityRequest
                {
                    RequestId = Guid.NewGuid(),
                    EntityId  = (int)destroyCmd.NetworkId,
                };

                _writer.Write(request);

                FdpLog<DestroyEntityCommandEgressTranslator>.Debug(
                    "[Node-{0}] DestroyEntityCommand \u2192 DeleteEntityRequest NetID={1} reason={2}",
                    _localNodeId, destroyCmd.NetworkId, destroyCmd.Reason!);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}

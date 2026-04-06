using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using ModuleHost.Core.Abstractions;

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

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public DestroyEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus)
            : this(new DdsWriterAdapter<DeleteEntityRequest>(participant, DdsTopicName), eventBus)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal DestroyEntityCommandEgressTranslator(
            IDdsWriter<DeleteEntityRequest> writer,
            FdpEventBus eventBus)
        {
            _writer   = writer    ?? throw new ArgumentNullException(nameof(writer));
            _eventBus = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
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
                    "[Egress] DestroyEntityCommand → DeleteEntityRequest NetID={0} reason={1}",
                    destroyCmd.NetworkId, destroyCmd.Reason);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}

using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.NetworkSpawning.Events;
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
    /// ⭐ <c>CE-2247</c> — the "if you don't own it, request it" half of the one teardown (Entity Creation §3.4c): a node that
    /// destroys an entity whose MASTER it does not own cannot dispose it on the wire (only the master's owner writes the
    /// dispose), so its replicas elsewhere would stay. This translator sends such a destroy to the owner as a
    /// <see cref="DeleteEntityRequest"/>; the owner's <c>DeleteEntityRequestSystem</c> tears it down and its dispose removes
    /// it everywhere. A destroy on the master's owner is NOT forwarded (its own teardown disposes it), nor one that came from
    /// the network (<see cref="DestroyEntityCommand.IsRemote"/>). On EVERY node (R-3015 — it was the IG's alone; a charge
    /// set off on SimHost, which owns its position but not its master, stayed live on CGF and the IG).
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
        private readonly NetworkEntityMap? _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public DestroyEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus,
            NetworkEntityMap entityMap,
            long localNodeId = 0)
            : this(new DdsWriterAdapter<DeleteEntityRequest>(participant, DdsTopicName), eventBus, localNodeId, entityMap)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal DestroyEntityCommandEgressTranslator(
            IDdsWriter<DeleteEntityRequest> writer,
            FdpEventBus eventBus,
            long localNodeId = 0,
            NetworkEntityMap? entityMap = null)
        {
            _writer   = writer    ?? throw new ArgumentNullException(nameof(writer));
            _eventBus = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId = localNodeId;
            _entityMap   = entityMap;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// ⭐ <c>CE-2247</c> — the work is done in the EGRESS hook, as its <see cref="Direction"/> says: a node that routes translators by
        /// direction (<c>NedReplicationModule</c>) never calls an egress translator's <see cref="PollIngress"/> — it ran only on the IG,
        /// whose hand-built list went to both systems.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            foreach (var destroyCmd in _eventBus.ReadManaged<DestroyEntityCommand>())
            {
                // Prevent echo loop: do not forward bottom-up disposal notifications back to
                // the server.  When SimHost sends EntityMaster DISPOSE the IG publishes a
                // DestroyEntityCommand marked as remote; forwarding it would send a second
                // DeleteEntityRequest for an already-deleted entity (error code 3).
                if (destroyCmd.IsRemote) continue;
                if (OwnsMaster(view, destroyCmd.NetworkId)) continue;   // CE-2247: its own teardown disposes it

                var request = new DeleteEntityRequest
                {
                    RequestId = Guid.NewGuid(),
                    EntityId  = (int)destroyCmd.NetworkId,
                };

                _writer.Write(request);
                SentSampleCount++;

                FdpLog<DestroyEntityCommandEgressTranslator>.Debug(
                    "[Node-{0}] DestroyEntityCommand \u2192 DeleteEntityRequest NetID={1} reason={2}",
                    _localNodeId, destroyCmd.NetworkId, destroyCmd.Reason!);
            }
        }

        /// <summary>This node owns the entity's master, so its ELM teardown writes the dispose itself. Unknown ⇒ false (forward).</summary>
        private bool OwnsMaster(ISimulationView view, long networkId)
            => _entityMap != null && _entityMap.TryGetEntity(networkId, out var entity) && view.IsAlive(entity)
               && view.HasAuthority(entity, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)Hrot.NED.Descriptors.EDescriptorType.dtEntityMaster, 0));


        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}

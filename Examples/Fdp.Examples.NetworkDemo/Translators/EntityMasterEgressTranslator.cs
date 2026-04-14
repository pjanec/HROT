using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Examples.NetworkDemo.Descriptors;
using Fdp.ModuleHost.Core.Abstractions;
using Fdp.ModuleHost.Core.Network;
using Fdp.ModuleHost.Network.Cyclone.Services;
using Fdp.ModuleHost.Network.Cyclone.Topics;

namespace Fdp.Examples.NetworkDemo.Translators
{
    /// <summary>
    /// Egress-only translator that publishes <see cref="EntityMasterTopic"/> to the network
    /// whenever a locally-owned entity is spawned.  Remote nodes receive these announcements
    /// and create ghost entities via <see cref="EntityMasterIngressTranslator"/>.
    /// </summary>
    public class EntityMasterEgressTranslator : Fdp.Interfaces.IDescriptorTranslator
    {
        private readonly DdsWriter<EntityMasterTopic> _writer;
        private readonly NodeIdMapper _nodeMapper;
        private readonly int _localInternalId;

        // Track which NetIDs have already been published to avoid spamming.
        // EntityMasterTopic uses TransientLocal QoS – once published DDS will
        // deliver it to any late-joining subscriber automatically.
        private readonly HashSet<long> _publishedNetIds = new();

        public string TopicName => "SST_EntityMaster";
        public long DescriptorOrdinal => DemoDescriptors.Master;

        public EntityMasterEgressTranslator(
            DdsParticipant participant,
            NodeIdMapper nodeMapper,
            int localInternalId)
        {
            _writer = new DdsWriter<EntityMasterTopic>(participant);
            _nodeMapper = nodeMapper ?? throw new ArgumentNullException(nameof(nodeMapper));
            _localInternalId = localInternalId;
        }

        /// <summary>Ingress is not used by this translator.</summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Scans for locally-owned entities with a <see cref="TkbIdentity"/> component
        /// and publishes an <see cref="EntityMasterTopic"/> sample for each one that has not
        /// yet been announced.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            // Locally-spawned entities start in Constructing lifecycle (set by NetworkSpawningSystem).
            // We must include all lifecycle states or the EntityMasterTopic will never be published
            // for newly-spawned entities.
            var repo = view as EntityRepository;

            var query = view.Query()
                .With<NetworkIdentity>()
                .With<TkbIdentity>()
                .With<NetworkOwnership>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                if (ownership.PrimaryOwnerId != _localInternalId)
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (_publishedNetIds.Contains(netId.Value))
                    continue;

                ref readonly var tkbId = ref view.GetComponentRO<TkbIdentity>(entity);

                // Read DisType from entity header (stored natively by NetworkSpawningSystem).
                ulong disType = repo != null ? repo.GetHeader(entity.Index).DisType.Value : 0UL;

                NetworkAppId ownerId;
                try
                {
                    ownerId = _nodeMapper.GetExternalId(_localInternalId);
                }
                catch (Exception ex)
                {
                    FdpLog<EntityMasterEgressTranslator>.Warn(
                        "[EntityMasterEgress] Failed to resolve external ID for local node {0}: {1}",
                        _localInternalId, ex.Message);
                    continue;
                }

                _writer.Write(new EntityMasterTopic
                {
                    EntityId    = netId.Value,
                    OwnerId     = ownerId,
                    TkbTypeValue = tkbId.TkbType,
                    DisTypeValue = disType,
                    Flags       = 0
                });

                _publishedNetIds.Add(netId.Value);

                FdpLog<EntityMasterEgressTranslator>.Debug(
                    "[EntityMasterEgress] Published EntityMaster for NetID={0}", netId.Value);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            _writer.DisposeInstance(new EntityMasterTopic { EntityId = networkEntityId });
            _publishedNetIds.Remove(networkEntityId);
        }
    }
}

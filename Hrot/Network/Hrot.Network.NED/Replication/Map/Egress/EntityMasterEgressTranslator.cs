using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that publishes <see cref="EntityMaster"/> DDS samples
    /// from FDP-internal network components.
    /// </summary>
    public class EntityMasterEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityMaster> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;
        private readonly HashSet<long> _publishedNetIds = new();

        public string TopicName => "EntityMaster";
        public long DescriptorOrdinal => 0;

        // Targets: NetworkIdentity (50) + TkbIdentity (65)
        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.NetworkIdentity, GlobalComponentIds.TkbIdentity };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public EntityMasterEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            long localNodeId)
        {
            _writer = new DdsWriter<EntityMaster>(participant, "EntityMaster");
            _entityMap = entityMap;
            _localNodeId = localNodeId;
        }

        /// <summary>
        /// Egress-only translator; ingress is not used.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Publishes EntityMaster for all locally-owned entities that have
        /// the required network identity and TKB identity components.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var repo = view as EntityRepository;

            var query = view.Query()
                .With<NetworkIdentity>()
                .With<TkbIdentity>()
                // Lifecycle.All is crucial: Constructing entities must publish EntityMaster
                // so remote peers can create ghosts and ACK the construction.
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Authority check: replaced PrimaryOwnerId == LocalNodeId.
                // Enables split-ownership scenarios.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // EntityMaster is lifecycle-only metadata, so enforce strict exactly-once egress.
                if (_publishedNetIds.Contains(netId.Value))
                    continue;

                ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);

                // Read DisType from entity header (written natively by NetworkSpawningSystem).
                var dis = repo != null
                    ? repo.GetHeader(entity.Index).DisType
                    : default;

                _writer.Write(new EntityMaster
                {
                    EntityId = (int)netId.Value,
                    TkbType = tkb.TkbType,
                    DisType = new DisTypeStruct
                    {
                        Kind        = dis.Kind,
                        Domain      = dis.Domain,
                        Country     = dis.Country,
                        Category    = dis.Category,
                        Subcategory = dis.Subcategory,
                        Specific    = dis.Specific,
                        Extra       = dis.Extra,
                    },
                    Flags = 0
                });

                _publishedNetIds.Add(netId.Value);
                FdpLog<EntityMasterEgressTranslator>.Debug(
                    "[Node-{0}] Egress: Writing EntityMaster for NetID={1}", _localNodeId, netId.Value);
            }
        }

        /// <summary>
        /// Ghost promotion does not apply to EntityMaster on egress.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>
        /// Sends a DDS dispose for the named EntityMaster instance.
        /// </summary>
        public void Dispose(long networkEntityId)
        {
            _writer.DisposeInstance(new EntityMaster { EntityId = (int)networkEntityId });
            _publishedNetIds.Remove(networkEntityId);
        }
    }
}

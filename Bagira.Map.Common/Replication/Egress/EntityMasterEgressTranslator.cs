using System.Collections.Generic;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using NetworkOwnership = ModuleHost.Core.Network.NetworkOwnership;

namespace Bagira.Map.Common.Replication.Egress
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
        private readonly HashSet<long> _tracedNetIds = new();

        public string TopicName => "EntityMaster";
        public long DescriptorOrdinal => 0;

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
        /// the required network identity and spawn request components.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<NetworkOwnership>()
                .With<NetworkSpawnRequest>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var spawn = ref view.GetComponentRO<NetworkSpawnRequest>(entity);

                _writer.Write(new EntityMaster
                {
                    EntityId = (int)netId.Value,
                    TkbType = spawn.TkbType,
                    DisType = spawn.DisType,
                    Flags = 0
                });

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<EntityMasterEgressTranslator>.Debug(
                        "[TRACE-SH] Egress: Writing EntityMaster for NetID={0}", netId.Value);
                }
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
        }
    }
}

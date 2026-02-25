using Bagira.BDC.SSTD;
using Bagira.SimHost.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.SimHost.Translators
{
    /// <summary>
    /// Egress translator: publishes <c>EntityMission</c> DDS topic whenever the
    /// <see cref="EntityMissionHolder"/> managed component on a locally-owned entity
    /// has been written since the last publish cycle.
    ///
    /// <para>
    /// The translator uses table-level dirty tracking
    /// (<see cref="EntityRepository.HasComponentChanged"/>) as a fast early-out.
    /// Only entities whose <see cref="NetworkAuthority.HasAuthority"/> flag is
    /// <c>true</c> are published, preventing a broadcast loop with remote peers.
    /// </para>
    ///
    /// <para>
    /// Ingress is handled by <see cref="EntityMissionTranslator"/>.
    /// </para>
    /// </summary>
    public class EntityMissionEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityMission> _writer;
        private readonly NetworkEntityMap         _entityMap;

        /// <summary>
        /// The <see cref="EntityRepository.GlobalVersion"/> recorded at the end of the
        /// last <see cref="ScanAndPublish"/> cycle. Used to determine whether any
        /// <see cref="EntityMissionHolder"/> writes occurred since the last publish.
        /// </summary>
        private uint _lastPublishedVersion;

        public string TopicName        => "EntityMission";
        public long   DescriptorOrdinal => 51;

        public EntityMissionEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            _writer    = new DdsWriter<EntityMission>(participant, "EntityMission");
            _entityMap = entityMap;
        }

        /// <summary>
        /// Ingress is handled by <see cref="EntityMissionTranslator"/>; no-op here.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Scans all locally-owned entities that carry an <see cref="EntityMissionHolder"/>
        /// and publishes their mission state to DDS when the component table is dirty.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            // Cast to concrete repo to access dirty-tracking and managed component APIs.
            var repo = (EntityRepository)view;

            // Table-level early-out: skip the query entirely when nothing changed.
            if (!repo.HasComponentChanged(typeof(EntityMissionHolder), _lastPublishedVersion))
            {
                _lastPublishedVersion = repo.GlobalVersion;
                return;
            }

            var query = view.Query()
                .WithManaged<EntityMissionHolder>()
                .With<NetworkAuthority>()
                .With<NetworkIdentity>()
                .Build();

            foreach (var entity in query)
            {
                ref readonly var netAuth = ref view.GetComponentRO<NetworkAuthority>(entity);
                if (!netAuth.HasAuthority)
                    continue;

                ref readonly var netId  = ref view.GetComponentRO<NetworkIdentity>(entity);
                var holder = view.GetManagedComponentRO<EntityMissionHolder>(entity);

                // Patch the wire EntityId so it always matches the network-layer identity.
                var mission = holder.Mission;
                mission.EntityId = netId.Value;

                _writer.Write(in mission);
            }

            _lastPublishedVersion = repo.GlobalVersion;
        }

        /// <summary>
        /// Applies a mission snapshot directly to the repository.
        /// Used by the replay / snapshot system.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityMission mission)
                repo.SetManagedComponent(entity, new EntityMissionHolder { Mission = mission });
        }

        /// <summary>
        /// Sends a DDS dispose for the named entity's mission instance, signalling
        /// remote subscribers that this host no longer owns the topic instance.
        /// </summary>
        public void Dispose(long networkEntityId)
        {
            _writer.DisposeInstance(new EntityMission { EntityId = networkEntityId });
        }
    }
}

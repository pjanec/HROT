using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.SimHost.Translators
{
    /// <summary>
    /// Egress translator: placeholder for future MissionPlanQueue → EntityMission
    /// publication. Currently a no-op; ingress is handled by
    /// <see cref="EntityMissionTranslator"/>.
    /// </summary>
    public class EntityMissionEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityMission> _writer;
        private readonly NetworkEntityMap         _entityMap;

        /// <summary>
        /// The <see cref="EntityRepository.GlobalVersion"/> recorded at the end of the
        /// last <see cref="ScanAndPublish"/> cycle.
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
        /// No-op placeholder. Updates dirty-tracking version to avoid repeated work.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            // Cast to concrete repo to access dirty-tracking and managed component APIs.
            var repo = (EntityRepository)view;

            if (!repo.HasComponentChanged(typeof(MissionPlanQueue), _lastPublishedVersion))
            {
                _lastPublishedVersion = repo.GlobalVersion;
                return;
            }

            _lastPublishedVersion = repo.GlobalVersion;
        }

        /// <summary>
        /// Applies a mission snapshot directly to the repository.
        /// Currently not supported for MissionPlanQueue egress.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
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

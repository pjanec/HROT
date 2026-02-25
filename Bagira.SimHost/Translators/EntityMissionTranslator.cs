using Bagira.BDC.SSTD;
using Bagira.SimHost.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.SimHost.Translators
{
    /// <summary>
    /// Ingress translator: subscribes to the DDS <c>EntityMission</c> topic and
    /// maintains the <see cref="EntityMissionHolder"/> managed ECS component on the
    /// matching entity.
    ///
    /// On a valid sample, <see cref="EntityMissionHolder.Mission"/> is set/updated.
    /// On <c>NOT_ALIVE_DISPOSED</c>, the component is removed.
    /// Unknown entity IDs (not yet registered in the <see cref="NetworkEntityMap"/>)
    /// are silently skipped.
    ///
    /// <para>
    /// EntityMission is a <em>managed</em> DDS struct (contains <c>List&lt;T&gt;</c>
    /// via <c>MissionPlan.Tasks</c>) and therefore cannot be stored as a Tier 1
    /// (unmanaged) ECS component.  <see cref="EntityMissionHolder"/> provides the
    /// Tier 2 class wrapper required by the ECS kernel.
    /// </para>
    /// </summary>
    public class EntityMissionTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<EntityMission> _reader;
        private readonly NetworkEntityMap         _entityMap;

        public string TopicName       => "EntityMission";
        public long   DescriptorOrdinal => 50;

        public EntityMissionTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            _reader    = new DdsReader<EntityMission>(participant, "EntityMission");
            _entityMap = entityMap;
        }

        /// <summary>
        /// Polls the DDS reader and queues managed-component set/remove commands.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();

            foreach (var sample in loan)
            {
                // Resolve entity ID from either valid data or the key-only native frame.
                long entityId;
                if (sample.IsValid)
                {
                    entityId = sample.Data.EntityId;
                }
                else
                {
                    // For NOT_ALIVE samples the managed .Data property throws.
                    // Read key fields directly from the native serialised buffer.
                    var keySample = EntityMission.FromNative(sample.NativePtr);
                    entityId = keySample.EntityId;
                }

                if (!_entityMap.TryGetEntity(entityId, out var entity))
                    continue; // Entity not yet known — skip safely

                if (sample.IsValid)
                {
                    cmd.SetManagedComponent(entity, new EntityMissionHolder { Mission = sample.Data });
                }
                else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                {
                    cmd.RemoveManagedComponent<EntityMissionHolder>(entity);
                }
            }
        }

        /// <summary>
        /// Egress is handled by <see cref="EntityMissionEgressTranslator"/>; no-op here.
        /// </summary>
        public void ScanAndPublish(ISimulationView view) { }

        /// <summary>
        /// Applies an <see cref="EntityMission"/> payload directly to the repository.
        /// Used by the replay / snapshot system.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityMission mission)
                repo.SetManagedComponent(entity, new EntityMissionHolder { Mission = mission });
        }

        /// <summary>
        /// This translator is ingress-only; no DDS instance to dispose.
        /// </summary>
        public void Dispose(long networkEntityId) { }
    }
}

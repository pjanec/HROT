using System;
using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Abstractions;

namespace Hrot.IG.Translators
{
    public class IgMissionIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<EntityMission> _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public long DescriptorOrdinal => 50;
        public string TopicName => "EntityMission";

        public IgMissionIngressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem)
        {
            _reader = new DdsReader<EntityMission>(participant);
            _entityMap = entityMap;
            _ghostCreationSystem = ghostCreationSystem;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();

            foreach (var sample in loan)
            {
                long entityId;
                if (sample.IsValid)
                {
                    entityId = sample.Data.EntityId;
                }
                else
                {
                    var keySample = EntityMission.FromNative(sample.NativePtr);
                    entityId = keySample.EntityId;
                }

                if (!_entityMap.TryGetEntity(entityId, out var entity))
                {
                    if (sample.IsValid)
                    {
                        var repo = view as EntityRepository;
                        if (repo == null) continue;
                        entity = _ghostCreationSystem.CreateGhost(repo, entityId, view.Tick);
                    }
                    else
                    {
                        continue;
                    }
                }

                var erepo = view as EntityRepository;
                if (erepo != null)
                {
                    if (sample.IsValid)
                    {
                        erepo.SetComponent(entity, new IgMissionHolder { Mission = sample.Data });
                        FdpLog<IgMissionIngressTranslator>.Debug("[TRACE-IG] Ingress: EntityMission Entity={0} Tasks={1}", sample.Data.EntityId, sample.Data.Plan.Tasks?.Count ?? 0);
                    }
                    else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                    {
                        erepo.RemoveComponent<IgMissionHolder>(entity);
                    }
                }
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityMission mission)
            {
                repo.SetComponent(entity, new IgMissionHolder { Mission = mission });
            }
        }

        public void Dispose(long networkEntityId) { }
    }
}
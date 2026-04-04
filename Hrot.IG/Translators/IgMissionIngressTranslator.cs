using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior.Components;
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
                        erepo.SetComponent(entity, MapToPlan(sample.Data));
                        FdpLog<IgMissionIngressTranslator>.Debug("[TRACE-IG] Ingress: EntityMission Entity={0} Tasks={1}", sample.Data.EntityId, sample.Data.Plan.Tasks?.Count ?? 0);
                    }
                    else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                    {
                        erepo.RemoveComponent<ActiveMissionPlan>(entity);
                    }
                }
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityMission mission)
            {
                repo.SetComponent(entity, MapToPlan(mission));
            }
        }

        public void Dispose(long networkEntityId) { }

        private static ActiveMissionPlan MapToPlan(EntityMission mission)
        {
            var domainPlan = new DomainMissionPlan
            {
                ActiveTaskId = mission.Plan.ActiveTaskId,
                Tasks        = mission.Plan.Tasks?.ConvertAll(t => new DomainMissionTask
                {
                    TaskId          = t.TaskId,
                    ExecutingEngine = t.ExecutingEngine ?? string.Empty,
                    BehaviorId      = t.BehaviorId      ?? string.Empty,
                    BehaviorParams  = t.BehaviorParams  ?? string.Empty,
                }) ?? new List<DomainMissionTask>()
            };
            return new ActiveMissionPlan { Plan = domainPlan };
        }
    }
}
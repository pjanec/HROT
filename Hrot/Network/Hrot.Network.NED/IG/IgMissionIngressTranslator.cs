using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.IG
{
    public class IgMissionIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<EntityMission> _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public long DescriptorOrdinal => 50;
        public string TopicName => "EntityMission";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public IgMissionIngressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem,
            long localNodeId)
        {
            _reader = new DdsReader<EntityMission>(participant);
            _entityMap = entityMap;
            _ghostCreationSystem = ghostCreationSystem;
            _localNodeId = localNodeId;
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
                    ReceivedSampleCount++;
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
                        FdpLog<IgMissionIngressTranslator>.Debug("[Node-{0}] Ingress: EntityMission Entity={1} Tasks={2}", _localNodeId, sample.Data.EntityId, sample.Data.Plan.Tasks?.Count ?? 0);
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

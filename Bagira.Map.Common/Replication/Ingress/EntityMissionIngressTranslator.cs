using System;
using System.Globalization;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Bagira.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator: subscribes to the DDS <c>EntityMission</c> topic and
    /// writes a <see cref="MissionPlanQueue"/> component for the matching entity.
    ///
    /// On a valid sample, the queue is rebuilt from the mission plan tasks.
    /// On <c>NOT_ALIVE_DISPOSED</c>, the component is removed.
    /// Unknown entity IDs (not yet registered in the <see cref="NetworkEntityMap"/>)
    /// are silently skipped.
    /// </summary>
    public class EntityMissionIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<EntityMission> _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly DoctrineRegistry _doctrineRegistry;

        public string TopicName => "EntityMission";
        public long DescriptorOrdinal => 50;

        public EntityMissionIngressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            DoctrineRegistry doctrineRegistry)
        {
            _reader = new DdsReader<EntityMission>(participant, "EntityMission");
            _entityMap = entityMap;
            _doctrineRegistry = doctrineRegistry ?? throw new ArgumentNullException(nameof(doctrineRegistry));
        }

        /// <summary>
        /// Polls the DDS reader and queues component set/remove commands.
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
                    var queue = BuildQueue(sample.Data);
                    cmd.SetComponent(entity, queue);
                }
                else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                {
                    cmd.RemoveComponent<MissionPlanQueue>(entity);
                }
            }
        }

        /// <summary>
        /// Egress is handled by <see cref="Replication.Egress.EntityMissionEgressTranslator"/>; no-op here.
        /// </summary>
        public void ScanAndPublish(ISimulationView view) { }

        /// <summary>
        /// Applies an <see cref="EntityMission"/> payload directly to the repository.
        /// Used by the replay / snapshot system.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is EntityMission mission)
            {
                var queue = BuildQueue(mission);
                repo.SetComponent(entity, queue);
            }
        }

        /// <summary>
        /// This translator is ingress-only; no DDS instance to dispose.
        /// </summary>
        public void Dispose(long networkEntityId) { }

        private MissionPlanQueue BuildQueue(in EntityMission mission)
        {
            var queue = new MissionPlanQueue
            {
                CurrentPhase = 0,
                PhaseElapsedSeconds = 0f
            };

            var tasks = mission.Plan.Tasks ?? new List<MissionTask>();
            int count = Math.Min(tasks.Count, MissionPlanQueue.MaxPhases);

            if (tasks.Count > MissionPlanQueue.MaxPhases)
            {
                FdpLog<EntityMissionIngressTranslator>.Warn(
                    "[MissionTranslator] Mission has {0} tasks; truncating to {1}.",
                    tasks.Count, MissionPlanQueue.MaxPhases);
            }

            for (int i = 0; i < count; i++)
            {
                var task = tasks[i];
                int doctrineId = ResolveDoctrineId(task.BehaviorId);
                var (trigger, param) = ResolveTrigger(task.Triggers);

                queue.Phases[i] = new MissionPhase
                {
                    DoctrineId = doctrineId,
                    Trigger = trigger,
                    TriggerParam = param
                };
            }

            queue.PhaseCount = (byte)count;
            return queue;
        }

        private int ResolveDoctrineId(string? behaviorId)
        {
            if (string.IsNullOrWhiteSpace(behaviorId))
                return 0;

            if (_doctrineRegistry.TryGetId(behaviorId, out int doctrineId))
                return doctrineId;

            // Fallback: if BehaviorId is a raw numeric string (legacy egress without registry)
            // treat it directly as the doctrine integer ID.
            if (int.TryParse(behaviorId, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int numericId) && numericId > 0)
                return numericId;

            FdpLog<EntityMissionIngressTranslator>.Warn(
                "[MissionTranslator] Unknown BehaviorId '{0}'; using doctrine 0 (Idle).",
                behaviorId);
            return 0;
        }

        private static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<DdsMissionTrigger>? triggers)
        {
            if (triggers == null || triggers.Count == 0)
                return (EcsMissionTrigger.TimerElapsed, float.MaxValue); // no trigger = hold phase indefinitely

            var trigger = triggers[0];
            var type = trigger.Type ?? string.Empty;

            return type switch
            {
                "TimerElapsed" => (EcsMissionTrigger.TimerElapsed, ParseTriggerParam(trigger.Params)),
                "ReachedDestination" => (EcsMissionTrigger.ReachedDestination, 0f),
                "HealthCritical" => (EcsMissionTrigger.HealthCritical, ParseTriggerParam(trigger.Params)),
                _ => (EcsMissionTrigger.TimerElapsed, 0f)
            };
        }

        private static float ParseTriggerParam(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0f;

            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
        }
    }
}

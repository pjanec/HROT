using System;
using System.Globalization;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;
using EcsMissionTrigger = Fdp.Toolkit.Behavior.Components.MissionTrigger;

namespace Hrot.Map.Common.Replication.Ingress
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
        private readonly BehaviorRegistry _behaviorRegistry;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public string TopicName => "EntityMission";
        public long DescriptorOrdinal => (long)EDescriptorType.dtEntityMission;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMissionIngressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            BehaviorRegistry behaviorRegistry,
            GhostCreationSystem ghostCreationSystem)
        {
            _reader = new DdsReader<EntityMission>(participant, "EntityMission");
            _entityMap = entityMap;
            _behaviorRegistry = behaviorRegistry ?? throw new ArgumentNullException(nameof(behaviorRegistry));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
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
                    ReceivedSampleCount++;
                }
                else
                {
                    // For NOT_ALIVE samples the managed .Data property throws.
                    // Read key fields directly from the native serialised buffer.
                    var keySample = EntityMission.FromNative(sample.NativePtr);
                    entityId = keySample.EntityId;
                }

                if (!_entityMap.TryGetEntity(entityId, out var entity))
                {
                    // Entity not yet known — create a ghost so mission data is not dropped.
                    if (sample.IsValid)
                    {
                        var repo = view as EntityRepository;
                        if (repo == null) continue;
                        entity = _ghostCreationSystem.CreateGhost(repo, entityId, view.Tick);
                    }
                    else
                    {
                        continue; // Dispose for completely unknown entity — nothing to do.
                    }
                }

                if (sample.IsValid)
                {
                    // Skip ingress for entities the local node has authority over.
                    // Without this check the local egress translator's own published
                    // EntityMission sample loops back through DDS and overwrites the
                    // ECS component set by MissionControlExecutionSystem in the same frame.
                    if (view.HasAuthority(entity)) continue;

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
                int behaviorId = ResolveBehaviorId(task.BehaviorId);
                var (trigger, param) = ResolveTrigger(task.Triggers);

                queue.Phases[i] = new MissionPhase
                {
                    BehaviorId = behaviorId,
                    Trigger = trigger,
                    TriggerParam = param
                };
            }

            queue.PhaseCount = (byte)count;
            return queue;
        }

        private int ResolveBehaviorId(string? behaviorName)
        {
            if (string.IsNullOrWhiteSpace(behaviorName))
                return 0;

            if (_behaviorRegistry.TryGetId(behaviorName, out int behaviorId))
                return behaviorId;

            // Fallback: if BehaviorId is a raw numeric string (legacy egress without registry)
            // treat it directly as the behavior integer ID.
            if (int.TryParse(behaviorName, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int numericId) && numericId > 0)
                return numericId;

            FdpLog<EntityMissionIngressTranslator>.Warn(
                "[MissionTranslator] Unknown BehaviorName '{0}'; using behavior 0 (Idle).",
                behaviorName);
            return 0;
        }

        /// <summary>
        /// Delegates to <see cref="Hrot.Map.Common.Helpers.MissionTriggerHelper.ResolveTrigger"/> (BUG2-DEBT-01).
        /// </summary>
        internal static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<DdsMissionTrigger>? triggers)
            => Helpers.MissionTriggerHelper.ResolveTrigger(triggers);
    }
}

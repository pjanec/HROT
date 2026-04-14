using System.Collections.Generic;
using System.Globalization;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.ModuleHost.Abstractions;

using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;
using EcsMissionTrigger = Fdp.Toolkit.Behavior.Components.MissionTrigger;
using Fdp.Interfaces;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Publishes <see cref="EntityMission"/> DDS samples by translating the
    /// internal <see cref="MissionPlanQueue"/> ECS component.
    /// Uses <see cref="AuthorityExtensions.HasAuthority"/> for ownership checks
    /// and <see cref="SmartEgressUtil"/> for dirty-tracked reliable egress.
    /// </summary>
    public class EntityMissionEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityMission> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly DoctrineRegistry? _doctrineRegistry;

        public string TopicName => "EntityMission";
        public long DescriptorOrdinal => 51;

        public EntityMissionEgressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap,
            DoctrineRegistry? doctrineRegistry = null)
        {
            _writer           = new DdsWriter<EntityMission>(participant, "EntityMission");
            _entityMap        = entityMap;
            _doctrineRegistry = doctrineRegistry;
        }

        /// <summary>
        /// Ingress is handled by <see cref="Replication.Ingress.EntityMissionIngressTranslator"/>; no-op here.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Scans all authority-owned entities that have a <see cref="MissionPlanQueue"/>
        /// and publishes any dirty descriptors to DDS.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<MissionPlanQueue>()
                // Include Constructing entities so remote nodes receive mission data immediately.
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.ModuleHost.Network.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Authority check: only publish if we own the mission descriptor for this entity.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                // Smart egress: EntityMission is RELIABLE — publish only on dirty.
                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var queue = ref view.GetComponentRO<MissionPlanQueue>(entity);

                _writer.Write(new EntityMission
                {
                    EntityId = netId.Value,
                    Plan     = BuildDdsPlan(in queue)
                });

                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
            }
        }

        /// <summary>
        /// Converts the internal <see cref="MissionPlanQueue"/> to a DDS <see cref="MissionPlan"/>.
        /// </summary>
        private MissionPlan BuildDdsPlan(in MissionPlanQueue queue)
        {
            var tasks = new List<MissionTask>(queue.PhaseCount);

            for (int i = 0; i < queue.PhaseCount; i++)
            {
                var phase = queue.Phases[i];

                string triggerType = phase.Trigger switch
                {
                    EcsMissionTrigger.DoctrineFinished    => "DoctrineFinished",
                    EcsMissionTrigger.HealthCritical      => "HealthCritical",
                    EcsMissionTrigger.UnderAttack         => "UnderAttack",
                    _                                     => "TimerElapsed"
                };

                // Resolve human-readable name; fall back to numeric string for legacy interop.
                string behaviorId = _doctrineRegistry != null
                    && _doctrineRegistry.TryGetName(phase.DoctrineId, out var resolvedName)
                    ? resolvedName
                    : phase.DoctrineId.ToString(CultureInfo.InvariantCulture);

                tasks.Add(new MissionTask
                {
                    TaskId          = System.Guid.Empty,
                    ExecutingEngine = "SimHost",
                    BehaviorId      = behaviorId,
                    BehaviorParams  = string.Empty,
                    Triggers        = new List<DdsMissionTrigger>
                    {
                        new DdsMissionTrigger
                        {
                            Type   = triggerType,
                            Params = phase.TriggerParam.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }
                    },
                    State = i == queue.CurrentPhase ? eTaskState.TASK_ACTIVE : eTaskState.TASK_PLANNED
                });
            }

            return new MissionPlan
            {
                ActiveTaskId = System.Guid.Empty,
                Tasks        = tasks
            };
        }

        /// <summary>
        /// Applies a mission snapshot directly to the repository (replay / snapshot path).
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>
        /// Sends a DDS dispose for the named entity's mission topic instance.
        /// </summary>
        public void Dispose(long networkEntityId)
        {
            _writer.DisposeInstance(new EntityMission { EntityId = networkEntityId });
        }
    }
}

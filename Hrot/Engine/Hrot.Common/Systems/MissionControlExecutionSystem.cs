using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Hrot.Core.Mission;
using Hrot.Common.Events;
using NedStatusCode = Hrot.NED.Messages.NedStatusCode;
using EcsMissionTrigger = Fdp.Toolkit.Behavior.Components.MissionTrigger;

namespace Hrot.Common.Systems
{
    /// <summary>
    /// Pure-ECS execution system for mission control requests.
    ///
    /// <para>
    /// <b>PACK-P001 refactor:</b> This class replaces the DDS-aware
    /// <c>MissionControlRequestSystem</c>. It consumes <see cref="MissionControlIntent"/>
    /// events from the bus (published by <c>MissionControlIngressTranslator</c>) and
    /// publishes <see cref="MissionControlAckEvent"/> (consumed by
    /// <c>MissionControlAckEgressTranslator</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Constraints met by this file (auditable via grep):</b>
    /// <list type="bullet">
    ///   <item>Zero <c>DdsReader</c> / <c>DdsWriter</c> / <c>DdsParticipant</c> references.</item>
    ///   <item>Zero <c>System.Text.Json</c> references —
    ///         JSON parsing lives in <see cref="MissionControlBehaviorParamsHelper"/>.</item>
    ///   <item>Zero <c>EntityMission</c> DDS-writer references —
    ///         replication is handled automatically by <c>EntityMissionEgressTranslator</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Retry logic:</b> When a target entity is not yet in <see cref="NetworkEntityMap"/>
    /// (possible race between <c>CreateEntityRequestSystem</c> and the following
    /// <c>NetworkSpawningSystem</c> registration), the intent is re-queued for up to
    /// <see cref="MaxEntityWaitFrames"/> frames before being rejected.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class MissionControlExecutionSystem : IEcsModuleSystem
    {
        private const string EntityNotFoundMessage = "ERR_ENTITY_NOT_FOUND";
        private const string VersionConflictMessage = "ERR_VERSION_CONFLICT";

        /// <summary>
        /// Entity Mission descriptor ordinal — must match
        /// <see cref="Hrot.Map.Common.Replication.Egress.EntityMissionEgressTranslator.DescriptorOrdinal"/>.
        /// </summary>
        private const long EntityMissionDescriptorOrdinal = 51L;

        /// <summary>
        /// Number of frames to retry a request whose target entity is not yet in the map.
        /// See docstring on the original <c>MissionControlRequestSystem</c> for rationale.
        /// </summary>
        private const int MaxEntityWaitFrames = 10;

        private readonly NetworkEntityMap _entityMap;
        private readonly BehaviorRegistry _behaviorRegistry;
        private readonly TacticalIntentMapperRegistry _mapperRegistry;

        private readonly Dictionary<long, long> _missionVersions = new();
        private readonly Dictionary<long, List<Guid>> _taskOrder = new();
        private readonly Queue<(MissionControlIntent Intent, int FramesLeft)> _retryQueue = new();

        /// <summary>Production constructor — creates from ambient services.</summary>
        public MissionControlExecutionSystem(
            NetworkEntityMap entityMap,
            BehaviorRegistry behaviorRegistry,
            TacticalIntentMapperRegistry mapperRegistry)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _behaviorRegistry = behaviorRegistry ?? throw new ArgumentNullException(nameof(behaviorRegistry));
            _mapperRegistry = mapperRegistry ?? throw new ArgumentNullException(nameof(mapperRegistry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(MissionControlExecutionSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // -- 1. Retry intents whose entity wasn't mapped yet --────────────
            int retryCount = _retryQueue.Count;
            for (int i = 0; i < retryCount; i++)
            {
                var (intent, framesLeft) = _retryQueue.Dequeue();

                if (_entityMap.TryGetEntity(intent.TargetEntityId, out _))
                {
                    FdpLog<MissionControlExecutionSystem>.Debug(
                        "[MissionControl] Retry succeeded for entity {0} (request {1}).",
                        intent.TargetEntityId, intent.RequestId);
                    ProcessIntent(repo, intent);
                }
                else if (framesLeft > 0)
                {
                    _retryQueue.Enqueue((intent, framesLeft - 1));
                }
                else
                {
                    FdpLog<MissionControlExecutionSystem>.Warn(
                        "[MissionControl] Entity {0} not found after {1} retry frames; rejecting request {2}.",
                        intent.TargetEntityId, MaxEntityWaitFrames, intent.RequestId);
                    PublishAck(repo, intent.RequestId, NedStatusCode.EntityNotFound, newVersion: 0);
                }
            }

            // ── 2. Process newly-arrived intents ─────────────────────────────
            var intents = repo.Bus.ReadManaged<MissionControlIntent>();
            foreach (var intent in intents)
            {
                ProcessIntent(repo, intent);
            }
        }

        private void ProcessIntent(EntityRepository repo, MissionControlIntent intent)
        {
            if (!_entityMap.TryGetEntity(intent.TargetEntityId, out var entity))
            {
                FdpLog<MissionControlExecutionSystem>.Debug(
                    "[MissionControl] Entity {0} not in map yet; queuing request {1} for retry.",
                    intent.TargetEntityId, intent.RequestId);
                _retryQueue.Enqueue((intent, MaxEntityWaitFrames));
                return;
            }

            long currentVersion = _missionVersions.TryGetValue(intent.TargetEntityId, out var version)
                ? version
                : 0;

            switch (intent.Payload.CommandType)          
            {
                case eMissionCommandType.CMD_REPLACE_MISSION:
                {
                    if (intent.BaseVersion > 0 && intent.BaseVersion != currentVersion)
                    {
                        PublishAck(repo, intent.RequestId, NedStatusCode.VersionConflict, newVersion: 0);
                        return;
                    }

                    var plan = intent.Payload.FullMissionData;
                    if (plan == null)
                    {
                        PublishAck(repo, intent.RequestId, NedStatusCode.NotSupported, newVersion: 0);
                        return;
                    }
                    plan.Tasks ??= new List<MissionTask>();
                    if (!TryBuildQueue(repo, plan, out var queue, out var orderedTaskIds))
                    {
                        FdpLog<MissionControlExecutionSystem>.Debug(
                            "[MissionControl] FollowRoute entity not ready; queuing request {0} for retry.",
                            intent.RequestId);
                        _retryQueue.Enqueue((intent, MaxEntityWaitFrames));
                        return;
                    }
                    var domainPlan = new DomainMissionPlan
                    {
                        ActiveTaskId = plan.ActiveTaskId,
                        Tasks = plan.Tasks?.ConvertAll(t => new DomainMissionTask
                        {
                            TaskId = t.TaskId,
                            ExecutingEngine = t.ExecutingEngine ?? string.Empty,
                            BehaviorName = t.BehaviorId ?? string.Empty,
                            BehaviorParams = t.BehaviorParams ?? string.Empty,
                            Triggers = t.Triggers?.ConvertAll(st => new DomainMissionTrigger
                            {
                                Type   = st.Type   ?? string.Empty,
                                Params = st.Params ?? string.Empty,
                            }) ?? new List<DomainMissionTrigger>(),
                        }) ?? new List<DomainMissionTask>()
                    };
                    repo.SetComponent(entity, queue);
                    repo.SetManagedComponent(entity, new ActiveMissionPlan
                    {
                        Plan = domainPlan
                    });
                    SmartEgressUtil.MarkDirty(repo, entity, EntityMissionDescriptorOrdinal);
                    _taskOrder[intent.TargetEntityId] = orderedTaskIds;

                    currentVersion++;
                    _missionVersions[intent.TargetEntityId] = currentVersion;

                    PublishAck(repo, intent.RequestId, NedStatusCode.Success, newVersion: currentVersion);
                    return;
                }

                case eMissionCommandType.CMD_JUMP_TO_TASK:
                {
                    if (!_taskOrder.TryGetValue(intent.TargetEntityId, out var orderedTaskIds))
                        orderedTaskIds = new List<Guid>();

                    int targetIndex = orderedTaskIds.IndexOf(intent.Payload.TargetTaskId);
                    if (targetIndex < 0)
                        targetIndex = 0;

                    if (!repo.HasComponent<MissionPlanQueue>(entity))
                        repo.AddComponent(entity, new MissionPlanQueue());

                    ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
                    queue.CurrentPhase = (byte)targetIndex;
                    queue.PhaseElapsedSeconds = 0f;

                    currentVersion++;
                    _missionVersions[intent.TargetEntityId] = currentVersion;

                    PublishAck(repo, intent.RequestId, NedStatusCode.Success, newVersion: currentVersion);
                    return;
                }

                case eMissionCommandType.CMD_ABORT_ALL:
                {
                    var abortQueue = new MissionPlanQueue
                    {
                        PhaseCount = 0,
                        CurrentPhase = 0,
                        PhaseElapsedSeconds = 0f
                    };
                    repo.SetComponent(entity, abortQueue);
                    repo.SetManagedComponent<ActiveMissionPlan>(entity, null!);
                    SmartEgressUtil.MarkDirty(repo, entity, EntityMissionDescriptorOrdinal);

                    _taskOrder[intent.TargetEntityId] = new List<Guid>();

                    repo.Bus.Publish(new ClearBehaviorEvent { Entity = entity });

                    currentVersion++;
                    _missionVersions[intent.TargetEntityId] = currentVersion;

                    PublishAck(repo, intent.RequestId, NedStatusCode.Success, newVersion: currentVersion);
                    return;
                }

                default:
                    PublishAck(repo, intent.RequestId, NedStatusCode.NotSupported, newVersion: 0);
                    return;
            }
        }

        private void PublishAck(EntityRepository repo, Guid requestId, NedStatusCode errorCode, long newVersion)
        {
            repo.Bus.Publish(new MissionControlAckEvent
            {
                RequestId = requestId,
                ErrorCode = (int)errorCode,
                NewVersion = newVersion,
            });
        }

        private bool TryBuildQueue(
            EntityRepository repo,
            MissionPlan plan,
            out MissionPlanQueue queue,
            out List<Guid> orderedTaskIds)
        {
            orderedTaskIds = new List<Guid>();

            queue = new MissionPlanQueue
            {
                CurrentPhase = 0,
                PhaseElapsedSeconds = 0f
            };

            var tasks = plan.Tasks ?? new List<MissionTask>();
            int count = Math.Min(tasks.Count, MissionPlanQueue.MaxPhases);

            if (tasks.Count > MissionPlanQueue.MaxPhases)
            {
                FdpLog<MissionControlExecutionSystem>.Warn(
                    "[MissionControl] Mission has {0} tasks; truncating to {1}.",
                    tasks.Count, MissionPlanQueue.MaxPhases);
            }

            Span<MissionPhase> phases = queue.Phases;
            for (int i = 0; i < count; i++)
            {
                var task = tasks[i];
                orderedTaskIds.Add(task.TaskId);

                if (task.BehaviorId == "FollowRoute")
                {
                    if (!MissionControlBehaviorParamsHelper.TryTranslateFollowRouteBehaviorParams(
                            repo, task.BehaviorParams, out string translated))
                        return false;

                    task.BehaviorParams = translated;
                    tasks[i] = task;
                }

                int behaviorId = ResolveBehaviorId(task.BehaviorId);
                var (trigger, param) = ResolveTrigger(task.Triggers);

                phases[i] = new MissionPhase
                {
                    BehaviorId = behaviorId,
                    Trigger = trigger,
                    TriggerParam = param
                };
            }

            queue.PhaseCount = (byte)count;
            return true;
        }

        private int ResolveBehaviorId(string? behaviorName)
        {
            if (string.IsNullOrWhiteSpace(behaviorName))
                return 0;

            if (_behaviorRegistry.TryGetId(behaviorName, out int behaviorId))
                return behaviorId;

            // Tactical intents (handled by TacticalIntentResolutionSystem) are not in the
            // concrete BehaviorRegistry. Falling back to 0 is correct; suppress the warning.
            if (_mapperRegistry.TryGetMapper(behaviorName, out _) )
                return 0;

            // Also suppress for pass-through intents (no mapper needed; TacticalIntentResolutionSystem
            // uses the intent ID directly as the behavior name via its fallback path).
            // We cannot enumerate all valid intent names here, so warn only for truly unknown IDs.
            FdpLog<MissionControlExecutionSystem>.Warn(
                "[MissionControl] Unknown BehaviorId '{0}'; using behavior 0 (Idle).",
                behaviorId);
            return 0;
        }

        /// <summary>
        /// Delegates to <see cref="Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger"/> — shared implementation.
        /// </summary>
        internal static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<Hrot.Core.Mission.MissionTrigger>? triggers)
            => Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger(triggers);

        // ── Test hooks ─────────────────────────────────────────────────────────

        /// <summary>Test hook: directly calls <see cref="ProcessIntent"/> bypassing bus.</summary>
        public void TestHook_ProcessIntent(EntityRepository repo, MissionControlIntent intent)
            => ProcessIntent(repo, intent);

        /// <summary>Test hook: number of intents currently in the retry queue.</summary>
        public int TestHook_RetryQueueCount => _retryQueue.Count;

        /// <summary>Test hook: run one retry-drain cycle (processes a snapshot of the queue).</summary>
        public void TestHook_DrainRetryQueue(EntityRepository repo)
        {
            int retryCount = _retryQueue.Count;
            for (int i = 0; i < retryCount; i++)
            {
                var (intent, framesLeft) = _retryQueue.Dequeue();
                if (_entityMap.TryGetEntity(intent.TargetEntityId, out _))
                {
                    ProcessIntent(repo, intent);
                }
                else if (framesLeft > 0)
                {
                    _retryQueue.Enqueue((intent, framesLeft - 1));
                }
                else
                {
                    PublishAck(repo, intent.RequestId, NedStatusCode.EntityNotFound, newVersion: 0);
                }
            }
        }
    }
}

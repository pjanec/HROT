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
    ///   <item>Zero <c>System.Text.Json</c> references — a task's params JSON passes through to the
    ///         behaviour, which converts it (CE-2145: FollowRoute's route lookup is the behaviour's own).</item>
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
    [TickMode(TickPolicy.EveryFrame)]
    public class MissionControlExecutionSystem : IEcsModuleSystem, IIntentClearProvider
    {
        private const string EntityNotFoundMessage = "ERR_ENTITY_NOT_FOUND";
        private const string VersionConflictMessage = "ERR_VERSION_CONFLICT";

        /// <summary>⭐ P-7 7c — the ack error code of a stale base version (<c>NedStatusCode.VersionConflict</c>), for a reader that does not see the wire enum.</summary>
        public const int VersionConflictCode = (int)NedStatusCode.VersionConflict;

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

        // ⭐ R-3039 S1 — each mission's version is on its queue (MissionPlanQueue.Version) and its task order on its plan
        //   (ActiveMissionPlan.TaskOrder), not here: a checkpoint restored into a fresh system carries both on.
        private readonly Queue<(MissionControlIntent Intent, int FramesLeft)> _retryQueue = new();
        private int _worldEpoch;   // ⭐ CE-3076 — per-id state belongs to ONE world (WorldEpoch, CE-2101)

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

        /// <inheritdoc/>
        public string Name => "MissionPlan";

        /// <summary>
        /// ⭐ <c>CE-3167</c> (Entity Commanding §2.5d, ruling 52) — an immediate order clears the unit's mission plan before it
        /// starts, so the plan cannot resume at its next phase (ruling 50: discarded, not paused). The same clear as
        /// <c>CMD_ABORT_ALL</c> WITHOUT the behaviour clear — the order's assign replaces the behaviour. A unit with no plan is
        /// left untouched (no version bump).
        /// </summary>
        public void ClearFor(Entity entity, EntityRepository repo)
        {
            bool hasQueue = repo.IsComponentTypeRegistered<MissionPlanQueue>() && repo.HasComponent<MissionPlanQueue>(entity)
                && repo.GetComponentRO<MissionPlanQueue>(entity).TaskCount > 0;
            bool hasPlan = repo.HasManagedComponent<ActiveMissionPlan>(entity);
            if (!hasQueue && !hasPlan) return;
            if (!_entityMap.TryGetNetworkId(entity, out int networkId)) networkId = 0;

            EmptyPlan(repo, entity, networkId);
            BumpVersion(repo, entity);
        }

        /// <summary>⭐ <c>CE-3167</c> — the plan emptied, replicated, and this system's task order forgotten.</summary>
        private void EmptyPlan(EntityRepository repo, Entity entity, long networkId)
        {
            // ⭐ R-3039 S1 — the version outlives the plan (it is on the queue); the task order goes with the plan (ActiveMissionPlan).
            repo.SetComponent(entity, new MissionPlanQueue { TaskCount = 0, CurrentTask = 0, TaskElapsedSeconds = 0f, Version = VersionOf(repo, entity) });
            repo.SetManagedComponent<ActiveMissionPlan>(entity, null!);
            SmartEgressUtil.MarkDirty(repo, entity, EntityMissionDescriptorOrdinal);
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(MissionControlExecutionSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // ⭐ CE-3076 (CE-2101's rule, DESIGN_Cluster_Load_Phase §8) — versions, task order and retries are keyed by net id,
            //   which the world boundary REUSES: a stale version would let a draft built on the last world's mission pass the
            //   version check, a stale task order would resolve CMD_JUMP_TO_TASK against the last world's list, and a queued
            //   retry would land on the new entity carrying that id. Only this system writes them, so clearing here is safe.
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch))
            {
                _retryQueue.Clear();
            }

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
                    PublishAck(repo, intent, NedStatusCode.EntityNotFound, newVersion: 0);
                }
            }

            // ── 2. Process newly-arrived intents ─────────────────────────────
            var intents = repo.ReadManagedEvents<MissionControlIntent>();
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

            // ⭐ Edit framework F6 (CE-4277) — only the node that owns the unit's BRAIN applies its plan (the tactical-intent rule): a local commit for a
            //   unit owned elsewhere goes there (MissionControlEgressTranslator) and is NOT written to this replica; a remote one on a bystander is silent.
            //   ⚠ Only a LOCAL intent is gated: a request that came off the wire (IsRemote) is executed as before (the ingress runs on the brain nodes) —
            //   the integration rails showed the brain-state authority bit is not a reliable owner test there.
            if (!intent.IsRemote && !OwnsBrain(repo, entity)) return;

            long currentVersion = VersionOf(repo, entity);

            switch (intent.Payload.CommandType)          
            {
                case eMissionCommandType.CMD_REPLACE_MISSION:
                {
                    if (intent.BaseVersion > 0 && intent.BaseVersion != currentVersion)
                    {
                        PublishAck(repo, intent, NedStatusCode.VersionConflict, newVersion: 0);
                        return;
                    }

                    var plan = intent.Payload.FullMissionData;
                    if (plan == null)
                    {
                        PublishAck(repo, intent, NedStatusCode.NotSupported, newVersion: 0);
                        return;
                    }
                    plan.Tasks ??= new List<MissionTask>();
                    bool built;
                    MissionPlanQueue queue;
                    List<Guid> orderedTaskIds;
                    try { built = TryBuildQueue(repo, plan, entity, intent.TargetEntityId, out queue, out orderedTaskIds); }
                    catch (Hrot.Core.Mission.MissionPlanException ex)
                    {
                        // ⭐ CE-4157 — refuse the whole request: no queue is applied, the entity keeps its plan
                        FdpLog<MissionControlExecutionSystem>.Error("[MissionControl] Request {0} refused: {1}", intent.RequestId, ex.Message);
                        PublishAck(repo, intent, NedStatusCode.ValidationFailed, newVersion: 0);
                        return;
                    }
                    if (!built)
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
                    CarryProgress(repo, entity, domainPlan, ref queue);   // ⭐ CE-4300 (E1)
                    currentVersion++;
                    queue.Version = currentVersion;
                    repo.SetComponent(entity, queue);
                    repo.SetManagedComponent(entity, new ActiveMissionPlan
                    {
                        Plan = domainPlan,
                        TaskOrder = orderedTaskIds,
                    });
                    SmartEgressUtil.MarkDirty(repo, entity, EntityMissionDescriptorOrdinal);

                    PublishAck(repo, intent, NedStatusCode.Success, newVersion: currentVersion);
                    return;
                }

                case eMissionCommandType.CMD_JUMP_TO_TASK:
                {
                    var orderedTaskIds = repo.HasManagedComponent<ActiveMissionPlan>(entity)
                        ? ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity)?.TaskOrder ?? System.Array.Empty<Guid>()
                        : System.Array.Empty<Guid>();

                    int targetIndex = System.Linq.Enumerable.ToList(orderedTaskIds).IndexOf(intent.Payload.TargetTaskId);
                    if (targetIndex < 0)
                        targetIndex = 0;

                    if (!repo.HasComponent<MissionPlanQueue>(entity))
                        repo.AddComponent(entity, new MissionPlanQueue());

                    ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
                    queue.CurrentTask = (byte)targetIndex;
                    queue.TaskElapsedSeconds = 0f;
                    // ⭐ CE-483 — the operator's answer to a halted (faulted) plan: run from the target phase again.
                    queue.Halted = 0;
                    if (targetIndex < MissionPlanQueue.MaxTasks)
                    {
                        Span<MissionPhaseOutcome> outcomes = queue.Outcomes;
                        outcomes[targetIndex] = MissionPhaseOutcome.None;
                    }

                    currentVersion++;
                    queue.Version = currentVersion;

                    PublishAck(repo, intent, NedStatusCode.Success, newVersion: currentVersion);
                    return;
                }

                case eMissionCommandType.CMD_ABORT_ALL:
                {
                    EmptyPlan(repo, entity, intent.TargetEntityId);   // ⭐ CE-3167 — the one plan clear, shared with ClearFor

                    repo.Bus.Publish(new ClearBehaviorEvent { Entity = entity, Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Operator });   // ⭐ CE-3034 — mission-control abort (R-193)

                    currentVersion++;
                    repo.GetComponentRW<MissionPlanQueue>(entity).Version = currentVersion;   // EmptyPlan left the queue on

                    PublishAck(repo, intent, NedStatusCode.Success, newVersion: currentVersion);
                    return;
                }

                default:
                    PublishAck(repo, intent, NedStatusCode.NotSupported, newVersion: 0);
                    return;
            }
        }

        /// <summary>⭐ R-3039 S1 — the mission's version, on its queue (0: none yet).</summary>
        /// <summary>⭐ P-7 7c — the plan's version as THIS node knows it (a commit's precondition): the ONE definition, read by the mission facade's snapshot too.
        /// ⚠ Only the brain's owner bumps it, and it is not on the wire: a replica reads 0 (= no precondition).</summary>
        public static long VersionOf(EntityRepository repo, Entity entity) =>
            repo.HasComponent<MissionPlanQueue>(entity) ? repo.GetComponentRO<MissionPlanQueue>(entity).Version : 0;

        private static void BumpVersion(EntityRepository repo, Entity entity)
        {
            if (!repo.HasComponent<MissionPlanQueue>(entity)) repo.AddComponent(entity, new MissionPlanQueue());
            repo.GetComponentRW<MissionPlanQueue>(entity).Version++;
        }

        /// <summary>Does this node own <paramref name="e"/>'s brain? An entity with no authority component (a networkless world, a bare test world) is local.</summary>
        public static bool OwnsBrain(EntityRepository repo, Entity e)
            => Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e);   // the primary owner (an entity with no authority component is local)

        private void PublishAck(EntityRepository repo, MissionControlIntent intent, NedStatusCode errorCode, long newVersion)
        {
            repo.Bus.Publish(new MissionControlAckEvent
            {
                RequestId = intent.RequestId,
                ErrorCode = (int)errorCode,
                NewVersion = newVersion,
            });
            // ⭐ Edit framework F6 (📄 docs/UX/DESIGN_Edit_Framework.md §2.1, §3, Q3) — the commit's answer is ALSO the one edit outcome: a failed version
            //   precondition is a CONFLICT (the user picks Reload or Overwrite), not a plain refusal.
            var (status, reason) = errorCode switch
            {
                NedStatusCode.Success => (Fdp.Toolkit.Replication.Edits.EditStatus.Applied, "the plan is in force"),
                NedStatusCode.VersionConflict => (Fdp.Toolkit.Replication.Edits.EditStatus.Conflict, "the plan was changed by someone else since you opened it"),
                NedStatusCode.EntityNotFound => (Fdp.Toolkit.Replication.Edits.EditStatus.Refused, "the unit is not known on its owner"),
                NedStatusCode.NotSupported => (Fdp.Toolkit.Replication.Edits.EditStatus.Refused, "this mission command is not supported"),
                NedStatusCode.ValidationFailed => (Fdp.Toolkit.Replication.Edits.EditStatus.Refused, "the plan did not validate"),
                _ => (Fdp.Toolkit.Replication.Edits.EditStatus.Refused, errorCode.ToString()),
            };
            Fdp.Toolkit.Replication.Edits.EditOutcomes.Report(repo, intent.RequestId, intent.TargetEntityId, status, reason);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4300</c> — mission plan E1 (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8, R-4161): a replace carries the plan's PROGRESS over BY
        /// TASK ID — ⛔ it used to restart the plan from task 1 (fixing a typo in task 4 sent a platoon back to its start line). A task already ended
        /// keeps its outcome; the RUNNING task, if it is still in the plan and unchanged (name, params, conditions), keeps running — same task,
        /// its elapsed time and halt kept, and the adapter does not restart it (it keys on the task, not the index); if its content changed it
        /// restarts; if it was removed, the plan goes on with the task after the last one that ended. A plan with nothing done starts at task 1.
        /// </summary>
        private static void CarryProgress(EntityRepository repo, Entity entity, DomainMissionPlan next, ref MissionPlanQueue queue)
        {
            if (!repo.HasComponent<MissionPlanQueue>(entity) || !repo.HasManagedComponent<ActiveMissionPlan>(entity)) return;
            var old = ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity)?.Plan;
            if (old?.Tasks is not { Count: > 0 } oldTasks) return;
            ref readonly var was = ref repo.GetComponentRO<MissionPlanQueue>(entity);

            Span<MissionPhaseOutcome> outcomes = queue.Outcomes;
            int lastEnded = -1, running = -1;
            for (int i = 0; i < queue.TaskCount && i < next.Tasks.Count; i++)
            {
                int j = IndexOf(oldTasks, next.Tasks[i].TaskId, was.TaskCount);
                if (j < 0) continue;
                if (was.Outcomes[j] != MissionPhaseOutcome.None)
                {
                    outcomes[i] = was.Outcomes[j];
                    lastEnded = i;
                }
                if (j == was.CurrentTask && SameContent(oldTasks[j], next.Tasks[i])) running = i;
            }
            if (running >= 0)
            {
                outcomes[running] = MissionPhaseOutcome.None;   // still running: its end is not yet known
                queue.CurrentTask = (byte)running;
                queue.TaskElapsedSeconds = was.TaskElapsedSeconds;
                queue.Halted = was.Halted;
            }
            else
                queue.CurrentTask = (byte)Math.Min(lastEnded + 1, queue.TaskCount);
        }

        private static int IndexOf(List<DomainMissionTask> tasks, Guid id, int count)
        {
            if (id == Guid.Empty) return -1;
            for (int j = 0; j < tasks.Count && j < count; j++)
                if (tasks[j].TaskId == id) return j;
            return -1;
        }

        private static bool SameContent(DomainMissionTask a, DomainMissionTask b)
        {
            if (a.BehaviorName != b.BehaviorName || a.BehaviorParams != b.BehaviorParams) return false;
            int n = a.Triggers?.Count ?? 0;
            if (n != (b.Triggers?.Count ?? 0)) return false;
            for (int i = 0; i < n; i++)
                if (a.Triggers![i].Type != b.Triggers![i].Type || a.Triggers[i].Params != b.Triggers[i].Params) return false;
            return true;
        }

        private bool TryBuildQueue(
            EntityRepository repo,
            MissionPlan plan,
            Entity entity,
            long networkId,
            out MissionPlanQueue queue,
            out List<Guid> orderedTaskIds)
        {
            orderedTaskIds = new List<Guid>();

            queue = new MissionPlanQueue
            {
                CurrentTask = 0,
                TaskElapsedSeconds = 0f
            };

            var tasks = plan.Tasks ?? new List<MissionTask>();
            int count = Math.Min(tasks.Count, MissionPlanQueue.MaxTasks);

            if (tasks.Count > MissionPlanQueue.MaxTasks)
            {
                FdpLog<MissionControlExecutionSystem>.Warn(
                    "[MissionControl] Mission has {0} tasks; truncating to {1}.",
                    tasks.Count, MissionPlanQueue.MaxTasks);
            }

            Span<MissionTaskSlot> phases = queue.Tasks;
            for (int i = 0; i < count; i++)
            {
                var task = tasks[i];
                orderedTaskIds.Add(task.TaskId);

                // ⭐ CE-2145 — a FollowRoute task's params pass through unchanged: the behaviour converts its own route
                //   (CgfNodes.ResolveFollowRoute) and waits while it is not compiled. OC1-S001's sender-side rewrite is gone.

                int behaviorId = ResolveBehaviorId(task.BehaviorId);
                var (trigger, param) = ResolveTrigger(task.Triggers, $"{task.BehaviorId} ({task.TaskId})");
                // ⭐ CE-4298 rest (mission plan §5.9 E9) — a WAIT (no order, no behaviour) with no *then* resolves to "when done": it has nothing to finish, so it would end at once. The editor refuses it; a plan that
                //   arrives so anyway loads (the load goes on) with ONE warning naming the entity and the task. THE one place every plan's tasks are resolved (every load passes TryBuildQueue).
                if (string.IsNullOrWhiteSpace(task.BehaviorId) && trigger == EcsMissionTrigger.BehaviorFinished)
                    FdpLog<MissionControlExecutionSystem>.Warn(
                        "[MissionControl] Entity {0}: task {1} ({2}) is a Wait with no 'then' — it would end at once; give it a condition.",
                        EntityLabel(repo, entity, networkId), i + 1, task.TaskId);

                phases[i] = new MissionTaskSlot
                {
                    BehaviorId = behaviorId,
                    Trigger = trigger,
                    TriggerParam = param
                };
            }

            queue.TaskCount = (byte)count;
            return true;
        }

        /// <summary>The entity as the warnings name it: its name when it has one, with its network id (<c>"Alpha 1-1 (#12)"</c>).</summary>
        private static string EntityLabel(EntityRepository repo, Entity entity, long networkId)
        {
            string? name = repo.IsAlive(entity) && repo.HasComponent<Fdp.Core.EntityInfo>(entity)
                ? repo.GetComponentRO<Fdp.Core.EntityInfo>(entity).Name.ToString() : null;
            return string.IsNullOrEmpty(name) ? "#" + networkId : name + " (#" + networkId + ")";
        }

        private int ResolveBehaviorId(string? behaviorName)
        {
            if (string.IsNullOrWhiteSpace(behaviorName))
                return 0;

            if (_behaviorRegistry.TryGetId(behaviorName, out int behaviorId))
                return behaviorId;

            // ⭐ CE-4298 (mission plan D1, R-4160) — an ORDER task (`Order.<Kind>`) is resolved by the RECEIVER to its own class's behaviour
            //   (TacticalIntentResolutionSystem); its stored id is 0 by design. An order nobody declares still warns.
            if (behaviorName.StartsWith(Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentPrefix, StringComparison.Ordinal)
                && Hrot.Common.Orders.OrderMenu.TryGetByName(behaviorName.Substring(Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentPrefix.Length), out _))
                return 0;

            // Tactical intents (handled by TacticalIntentResolutionSystem) are not in the
            // concrete BehaviorRegistry. Falling back to 0 is correct; suppress the warning.
            if (_mapperRegistry.TryGetMapper(behaviorName, out _) )
                return 0;

            // Also suppress for pass-through intents (no mapper needed; TacticalIntentResolutionSystem
            // uses the intent ID directly as the behavior name via its fallback path).
            // We cannot enumerate all valid intent names here, so warn only for truly unknown IDs.
            FdpLog<MissionControlExecutionSystem>.Warn(
                "[MissionControl] Unknown BehaviorId '{0}'; using behavior 0 (Idle).",
                behaviorName);   // ⭐ CE-4298 — the NAME (it logged the id it had just failed to find, always 0)
            return 0;
        }

        /// <summary>
        /// Delegates to <see cref="Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger"/> — shared implementation.
        /// </summary>
        internal static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<Hrot.Core.Mission.MissionTrigger>? triggers, string? task)
            => Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger(triggers, task);

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
                    PublishAck(repo, intent, NedStatusCode.EntityNotFound, newVersion: 0);
                }
            }
        }
    }
}

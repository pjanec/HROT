using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Components;
using Hrot.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="IMissionEditorService"/> for the offline editor.
    ///
    /// <para>
    /// Doctrine filtering: intersects the per-TKB catalog from <see cref="DoctrineCatalog"/>
    /// with the live-registered names from <see cref="DoctrineRegistry"/> to avoid
    /// offering behaviours that are not currently installed in the engine.
    /// </para>
    ///
    /// <para>
    /// Commit workflow follows the TAP pattern: a <see cref="MissionControlIntent"/>
    /// is published to the local bus, and <see cref="PollAcks"/> must be called from
    /// the Editor update loop to resolve the pending <see cref="Task{T}"/> when
    /// <see cref="MissionControlAckEvent"/> arrives.
    /// </para>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class EditorMissionService : IMissionEditorService
    {
        private readonly FdpEventBus      _bus;
        private readonly EntityRepository _repo;
        private readonly DoctrineRegistry _registry;

        private readonly Dictionary<Guid, TaskCompletionSource<MissionCommitResult>> _pendingCommits = new();

        /// <param name="bus">Local FDP event bus.</param>
        /// <param name="repo">Entity repository for ECS component reads.</param>
        /// <param name="registry">Live doctrine registry used for filtering.</param>
        public EditorMissionService(FdpEventBus bus, EntityRepository repo, DoctrineRegistry registry)
        {
            _bus      = bus;
            _repo     = repo;
            _registry = registry;
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
        {
            var entity = _repo.GetEntityByIndex((int)entityId);
            if (entity.IsNull || !_repo.IsAlive(entity))
                return Array.Empty<string>();

            if (!_repo.HasComponent<TkbIdentity>(entity))
                return Array.Empty<string>();

            long tkbType = _repo.GetComponent<TkbIdentity>(entity).TkbType;
            var catalog  = DoctrineCatalog.GetValidDoctrines(tkbType);

            return catalog.Where(n => _registry.TryGetId(n, out _)).ToList();
        }

        /// <inheritdoc/>
        public (Hrot.Core.Mission.MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
        {
            var entity = _repo.GetEntityByIndex((int)entityId);
            if (entity.IsNull || !_repo.IsAlive(entity))
                return (null, 0);

            if (!_repo.HasComponent<ActiveMissionPlan>(entity))
                return (null, 0);

            var amp  = _repo.GetComponent<ActiveMissionPlan>(entity);
            var plan = MapDomainPlanToNeutral(amp.Plan);
            return (plan, 0); // Version not stored in ECS; starts at 0.
        }

        /// <inheritdoc/>
        public System.Threading.Tasks.Task<MissionCommitResult> CommitMissionAsync(
            long entityId, Hrot.Core.Mission.MissionPlan plan, long baseVersion)
        {
            var tcs       = new TaskCompletionSource<MissionCommitResult>();
            var requestId = Guid.NewGuid();
            _pendingCommits[requestId] = tcs;

            _bus.PublishManaged(new MissionControlIntent
            {
                RequestId      = requestId,
                TargetEntityId = entityId,
                BaseVersion    = baseVersion,
                Payload = new MissionCommandUnion
                {
                    _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = MapNeutralPlanToNed(plan),
                }
            });

            return tcs.Task;
        }

        /// <inheritdoc/>
        public System.Threading.Tasks.Task<MissionCommitResult> SendControlCommandAsync(
            long entityId, Hrot.Core.Mission.eMissionCommandType type, Guid taskId)
        {
            var tcs       = new TaskCompletionSource<MissionCommitResult>();
            var requestId = Guid.NewGuid();
            _pendingCommits[requestId] = tcs;

            _bus.PublishManaged(new MissionControlIntent
            {
                RequestId      = requestId,
                TargetEntityId = entityId,
                BaseVersion    = 0,
                Payload = new MissionCommandUnion
                {
                    _d           = (eMissionCommandType)(int)type,
                    TargetTaskId = taskId,
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Consumes <see cref="MissionControlAckEvent"/> events from the bus and resolves any
        /// pending <see cref="CommitMissionAsync"/> or <see cref="SendControlCommandAsync"/> tasks.
        /// Must be called once per Editor update frame.
        /// </summary>
        public void PollAcks()
        {
            foreach (var ack in _bus.Consume<MissionControlAckEvent>())
            {
                if (_pendingCommits.TryGetValue(ack.RequestId, out var tcs))
                {
                    _pendingCommits.Remove(ack.RequestId);
                    bool success = ack.ErrorCode == 0;
                    tcs.TrySetResult(new MissionCommitResult(
                        Success:      success,
                        NewVersion:   ack.NewVersion,
                        ErrorMessage: success ? null : $"ErrorCode={ack.ErrorCode}"));
                }
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static Hrot.Core.Mission.MissionPlan MapDomainPlanToNeutral(DomainMissionPlan domain)
        {
            var tasks = new List<Hrot.Core.Mission.MissionTask>(domain.Tasks.Count);
            foreach (var dt in domain.Tasks)
            {
                tasks.Add(new Hrot.Core.Mission.MissionTask
                {
                    TaskId          = dt.TaskId,
                    BehaviorId      = dt.BehaviorId,
                    BehaviorParams  = dt.BehaviorParams,
                    ExecutingEngine = dt.ExecutingEngine,
                    State           = Hrot.Core.Mission.eTaskState.TASK_PLANNED,
                    Triggers        = new List<Hrot.Core.Mission.MissionTrigger>(),
                });
            }
            return new Hrot.Core.Mission.MissionPlan
            {
                ActiveTaskId = domain.ActiveTaskId,
                Tasks        = tasks,
            };
        }

        private static Hrot.NED.Descriptors.MissionPlan MapNeutralPlanToNed(Hrot.Core.Mission.MissionPlan plan)
        {
            var tasks = new List<Hrot.NED.Descriptors.MissionTask>(plan.Tasks?.Count ?? 0);
            if (plan.Tasks != null)
            {
                foreach (var t in plan.Tasks)
                {
                    tasks.Add(new Hrot.NED.Descriptors.MissionTask
                    {
                        TaskId          = t.TaskId,
                        BehaviorId      = t.BehaviorId,
                        BehaviorParams  = t.BehaviorParams,
                        ExecutingEngine = t.ExecutingEngine,
                        State           = (Hrot.NED.Descriptors.eTaskState)(int)t.State,
                        Triggers        = t.Triggers?.Select(x => new Hrot.NED.Descriptors.MissionTrigger
                                          { Type = x.Type, Params = x.Params }).ToList()
                                          ?? new List<Hrot.NED.Descriptors.MissionTrigger>(),
                    });
                }
            }
            return new Hrot.NED.Descriptors.MissionPlan
            {
                ActiveTaskId = plan.ActiveTaskId,
                Tasks        = tasks,
            };
        }
    }
}

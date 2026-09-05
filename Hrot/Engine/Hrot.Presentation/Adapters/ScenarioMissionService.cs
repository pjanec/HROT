using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Mission;
using Hrot.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Adapters
{
    /// <summary>
    /// Implements <see cref="IMissionEditorService"/> for the offline editor.
    ///
    /// <para>
    /// Behavior filtering: intersects the per-TKB catalog from <see cref="BehaviorCatalog"/>
    /// with the live-registered names from <see cref="BehaviorRegistry"/> to avoid
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
    public sealed class ScenarioMissionService : IMissionEditorService
    {
        private readonly FdpEventBus      _bus;
        private readonly EntityRepository _repo;
        private readonly BehaviorRegistry _registry;

        private readonly Dictionary<Guid, TaskCompletionSource<MissionCommitResult>> _pendingCommits = new();

        /// <param name="bus">Local FDP event bus.</param>
        /// <param name="repo">Entity repository for ECS component reads.</param>
        /// <param name="registry">Live behavior registry used for filtering.</param>
        public ScenarioMissionService(FdpEventBus bus, EntityRepository repo, BehaviorRegistry registry)
        {
            _bus      = bus;
            _repo     = repo;
            _registry = registry;
        }

        // ── Private entity resolution ─────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>BP-508</c> — routed through the ONE resolver *(<c>R-77</c>)*. ⛔ This copy used
        /// <c>GetComponent</c> *(a struct copy)* and had no null-repo guard.
        /// </summary>
        private Entity FindEntityByNetworkId(long networkId)
            => Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(_repo, networkId);

        /// <inheritdoc/>
        public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
        {
            var entity = FindEntityByNetworkId(entityId);
            if (entity.IsNull || !_repo.IsAlive(entity))
                return Array.Empty<string>();

            if (!_repo.HasComponent<TkbIdentity>(entity))
                return Array.Empty<string>();

            long tkbType = _repo.GetComponent<TkbIdentity>(entity).TkbType;
            var catalog  = BehaviorCatalog.GetValidBehaviors(tkbType);

            // Curated list: only names that are actually registered in the live registry.
            var result = catalog.Where(n => _registry.TryGetId(n, out _)).ToList();

            // Append editor-authored BTree behaviors not already in the curated list.
            AppendEditorBTreeBehaviors(_registry, result);

            return result;
        }

        /// <summary>
        /// Appends registered editor-authored BTree behaviors (BrainTier == BrainTierBTree) to
        /// <paramref name="result"/>, skipping any names already present (dedup, curated first).
        /// </summary>
        /// <remarks>
        /// INTERIM approach: all BrainTierBTree entries that are not in the curated list are
        /// included, making editor-authored BTrees available to every entity type.
        /// TODO (option c): gate by per-asset DisEntityType affinity mask instead of listing for all entity types.
        /// </remarks>
        private static void AppendEditorBTreeBehaviors(BehaviorRegistry registry, List<string> result)
        {
            var existingNames = new HashSet<string>(result, StringComparer.Ordinal);
            foreach (var name in registry.GetRegisteredNames())
            {
                if (existingNames.Contains(name))
                    continue;
                if (!registry.TryGetId(name, out int id))
                    continue;
                if (!registry.TryGetDefinition(id, out var def))
                    continue;
                if (def.BrainTier != BehaviorConstants.BrainTierBTree)
                    continue;

                result.Add(name);
                existingNames.Add(name); // keep dedup consistent if there are duplicates in the registry
            }
        }

        /// <inheritdoc/>
        public (Hrot.Core.Mission.MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
        {
            var entity = FindEntityByNetworkId(entityId);
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
                Payload = new MissionCommandPayload
                {
                    CommandType     = Hrot.Core.Mission.eMissionCommandType.CMD_REPLACE_MISSION,
                    FullMissionData = plan,
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
                Payload = new MissionCommandPayload
                {
                    CommandType  = type,
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
            foreach (var ack in _bus.Read<MissionControlAckEvent>())
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
                    BehaviorId      = dt.BehaviorName,
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
    }
}

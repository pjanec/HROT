using System;
using System.Collections.Generic;
using System.Text.Json;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common.Components;
using Bagira.Map.Common.Helpers;
using FDP.Toolkit.Replication.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Services;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Bagira.SimHost.Systems
{
    public class MissionControlRequestSystem : ComponentSystem
    {
        private const string EntityNotFoundMessage  = "ERR_ENTITY_NOT_FOUND";
        private const string VersionConflictMessage = "ERR_VERSION_CONFLICT";

        /// <summary>
        /// Number of frames to retry a mission request whose target entity is not yet
        /// registered in the <see cref="NetworkEntityMap"/>.
        ///
        /// Root cause: <c>CreateEntityRequestSystem</c> (Input phase) publishes
        /// <c>SpawnEntityCommand</c> to the event-bus write buffer and immediately sends
        /// <c>CreateEntityAck</c>.  The write buffer is not swapped until after
        /// <c>_kernel.Update()</c> completes, so <c>NetworkSpawningSystem</c> (BeforeSync)
        /// registers the entity in <c>NetworkEntityMap</c> only in the <em>following</em>
        /// frame.  Meanwhile the DDS loopback and the background <c>DdsCommandClient</c>
        /// listener thread can deliver the resulting <c>MissionControlRequest</c> before
        /// that next frame has had a chance to run, causing a false
        /// <c>ERR_ENTITY_NOT_FOUND</c> (Error=2) at 60 Hz.
        ///
        /// Ten frames ≈ 167 ms at 60 Hz — more than enough to cover the 1–2 frame lag.
        /// </summary>
        private const int MaxEntityWaitFrames = 10;

        private readonly DdsReader<MissionControlRequest> _reader;
        private readonly DdsWriter<MissionControlAck>     _writer;
        private readonly DdsWriter<EntityMission>         _missionStateWriter;
        private readonly NetworkEntityMap                 _entityMap;
        private readonly DoctrineRegistry                 _doctrineRegistry;
        private readonly Dictionary<long, long>           _missionVersions = new();
        private readonly Dictionary<long, List<Guid>>     _taskOrder = new();

        // Requests whose target entity wasn't in NetworkEntityMap yet; retried each frame.
        private readonly Queue<(MissionControlRequest Request, int FramesLeft)> _retryQueue = new();

        public MissionControlRequestSystem(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            DoctrineRegistry doctrineRegistry)
        {
            _reader             = new DdsReader<MissionControlRequest>(participant);
            _writer             = new DdsWriter<MissionControlAck>(participant);
            _missionStateWriter = new DdsWriter<EntityMission>(participant);
            _entityMap          = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _doctrineRegistry   = doctrineRegistry ?? throw new ArgumentNullException(nameof(doctrineRegistry));
        }

        /// <summary>Test hook: internal constructor that skips DDS setup for unit tests.</summary>
        internal MissionControlRequestSystem(
            NetworkEntityMap entityMap,
            DoctrineRegistry doctrineRegistry)
        {
            _reader             = null!;
            _writer             = null!;
            _missionStateWriter = null!;
            _entityMap          = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _doctrineRegistry   = doctrineRegistry ?? throw new ArgumentNullException(nameof(doctrineRegistry));
        }

        protected override void OnUpdate()
        {
            // ── 1. Retry requests whose entity wasn't in the map yet ──────────
            // Drain a snapshot of the current queue so newly-added retries are
            // not processed again in the same frame.
            int retryCount = _retryQueue.Count;
            for (int i = 0; i < retryCount; i++)
            {
                var (req, framesLeft) = _retryQueue.Dequeue();

                if (_entityMap.TryGetEntity(req.TargetEntityId, out _))
                {
                    // Entity is now registered — process normally.
                    FdpLog<MissionControlRequestSystem>.Debug(
                        "[MissionControl] Retry succeeded for entity {0} (request {1}).",
                        req.TargetEntityId, req.RequestId);
                    ProcessRequest(World, req);
                }
                else if (framesLeft > 0)
                {
                    _retryQueue.Enqueue((req, framesLeft - 1));
                }
                else
                {
                    // Give up — entity never materialised within the wait window.
                    FdpLog<MissionControlRequestSystem>.Warn(
                        "[MissionControl] Entity {0} not found after {1} retry frames; rejecting request {2}.",
                        req.TargetEntityId, MaxEntityWaitFrames, req.RequestId);
                    WriteAck(req.RequestId, SstStatusCode.EntityNotFound, EntityNotFoundMessage, newVersion: 0);
                }
            }

            // ── 2. Process newly-arrived DDS requests ─────────────────────────
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                ProcessRequest(World, sample.Data);
            }
        }

        private void ProcessRequest(EntityRepository repo, MissionControlRequest request)
        {
            if (!_entityMap.TryGetEntity(request.TargetEntityId, out var entity))
            {
                // Entity might not yet be registered (CreateEntityRequestSystem sends the ACK
                // before NetworkSpawningSystem has had a chance to run in the next frame).
                // Queue for retry rather than immediately returning ERR_ENTITY_NOT_FOUND.
                FdpLog<MissionControlRequestSystem>.Debug(
                    "[MissionControl] Entity {0} not in map yet; queuing request {1} for retry.",
                    request.TargetEntityId, request.RequestId);
                _retryQueue.Enqueue((request, MaxEntityWaitFrames));
                return;
            }

            long currentVersion = _missionVersions.TryGetValue(request.TargetEntityId, out var version)
                ? version
                : 0;

            switch (request.Payload._d)
            {
                case eMissionCommandType.CMD_REPLACE_MISSION:
                {
                    if (request.BaseVersion > 0 && request.BaseVersion != currentVersion)
                    {
                        WriteAck(request.RequestId, SstStatusCode.VersionConflict, VersionConflictMessage, newVersion: 0);
                        return;
                    }

                    var plan = request.Payload.FullMissionData;
                    plan.Tasks ??= new List<MissionTask>();

                    if (!TryBuildQueue(repo, plan, out var queue, out var orderedTaskIds))
                    {
                        // A FollowRoute task's route entity is not yet ready — retry.
                        FdpLog<MissionControlRequestSystem>.Debug(
                            "[MissionControl] FollowRoute entity not ready; queuing request {0} for retry.",
                            request.RequestId);
                        _retryQueue.Enqueue((request, MaxEntityWaitFrames));
                        return;
                    }

                    repo.SetComponent(entity, queue);
                    repo.SetComponent(entity, new Bagira.SimHost.Components.EntityMissionHolder
                    {
                        Mission = new Bagira.BDC.SSTD.EntityMission
                        {
                            EntityId = request.TargetEntityId,
                            Plan = plan
                        }
                    });
                    _taskOrder[request.TargetEntityId] = orderedTaskIds;

                    currentVersion++;
                    _missionVersions[request.TargetEntityId] = currentVersion;

                    WriteAck(request.RequestId, SstStatusCode.Success, errorMessage: null, newVersion: currentVersion);
                    PublishEntityMission(request.TargetEntityId, plan);
                    return;
                }

                case eMissionCommandType.CMD_JUMP_TO_TASK:
                {
                    if (!_taskOrder.TryGetValue(request.TargetEntityId, out var orderedTaskIds))
                        orderedTaskIds = new List<Guid>();

                    int targetIndex = orderedTaskIds.IndexOf(request.Payload.TargetTaskId);
                    if (targetIndex < 0)
                        targetIndex = 0;

                    if (!repo.HasComponent<MissionPlanQueue>(entity))
                        repo.AddComponent(entity, new MissionPlanQueue());

                    ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
                    queue.CurrentPhase = (byte)targetIndex;
                    queue.PhaseElapsedSeconds = 0f;

                    currentVersion++;
                    _missionVersions[request.TargetEntityId] = currentVersion;

                    WriteAck(request.RequestId, SstStatusCode.Success, errorMessage: null, newVersion: currentVersion);
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
                    repo.RemoveComponent<Bagira.SimHost.Components.EntityMissionHolder>(entity);

                    _taskOrder[request.TargetEntityId] = new List<Guid>();

                    // Publish ClearDoctrineEvent so DoctrineIngressSystem resets the entity's
                    // active doctrine to DoctrineIds.None (brain-death). This is a top-down
                    // forced abort — distinct from DoctrineFinishedEvent (natural completion).
                    // DoctrineIngressSystem guards against missing DoctrineState components.
                    World.Bus.Publish(new ClearDoctrineEvent { Entity = entity });

                    currentVersion++;
                    _missionVersions[request.TargetEntityId] = currentVersion;

                    WriteAck(request.RequestId, SstStatusCode.Success, errorMessage: null, newVersion: currentVersion);
                    return;
                }

                default:
                    WriteAck(request.RequestId, SstStatusCode.NotSupported, "ERR_NOT_SUPPORTED", newVersion: 0);
                    return;
            }
        }

        private void PublishEntityMission(long networkEntityId, MissionPlan plan)
        {
            _missionStateWriter?.Write(new EntityMission
            {
                EntityId = networkEntityId,
                Plan     = plan,
            });
        }

        private void WriteAck(Guid requestId, SstStatusCode errorCode, string? errorMessage, long newVersion)
        {
            _writer?.Write(new MissionControlAck
            {
                RequestId    = requestId,
                ErrorCode    = (int)errorCode,
                ErrorMessage = errorMessage,
                NewVersion   = newVersion
            });
        }

        /// <summary>
        /// Builds the <see cref="MissionPlanQueue"/> for <paramref name="plan"/>.
        /// Returns <c>false</c> when a <c>FollowRoute</c> task references a route entity whose
        /// <see cref="RouteTrajectoryCache"/> is not yet compiled; the caller should re-enqueue
        /// the request for retry instead of committing the plan.
        /// When <c>true</c> is returned the <paramref name="plan"/> Tasks may have been mutated
        /// to replace <c>routeEntityId</c> with the resolved <c>trajectoryId</c> in
        /// <c>BehaviorParams</c>.
        /// </summary>
        private bool TryBuildQueue(EntityRepository repo, MissionPlan plan, out MissionPlanQueue queue, out List<Guid> orderedTaskIds)
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
                FdpLog<MissionControlRequestSystem>.Warn(
                    "[MissionControl] Mission has {0} tasks; truncating to {1}.",
                    tasks.Count, MissionPlanQueue.MaxPhases);
            }

            for (int i = 0; i < count; i++)
            {
                var task = tasks[i];
                orderedTaskIds.Add(task.TaskId);

                // OC1-S001: translate network routeEntityId → local trajectoryId for FollowRoute tasks.
                if (task.BehaviorId == "FollowRoute")
                {
                    if (!TryTranslateFollowRouteBehaviorParams(repo, task.BehaviorParams, out string translated))
                        return false; // route entity not ready; caller must retry

                    task.BehaviorParams = translated;
                    tasks[i] = task; // struct copy back into the list
                }

                int doctrineId = ResolveDoctrineId(task.BehaviorId);
                var (trigger, param) = ResolveTrigger(task.Triggers);

                queue.Phases[i] = new MissionPhase
                {
                    DoctrineId   = doctrineId,
                    Trigger      = trigger,
                    TriggerParam = param
                };
            }

            queue.PhaseCount = (byte)count;
            return true;
        }

        /// <summary>
        /// Attempts to rewrite <c>BehaviorParams</c> for a <c>FollowRoute</c> task by
        /// resolving the <c>routeEntityId</c> (network ID) to a compiled
        /// <see cref="RouteTrajectoryCache.TrajectoryId"/> (local ECS ID).
        /// Returns <c>true</c> when the rewrite succeeded or when the params do not contain
        /// a <c>routeEntityId</c> key (pass-through).  Returns <c>false</c> when the route
        /// entity is not yet present or its trajectory has not been compiled.
        /// </summary>
        internal static bool TryTranslateFollowRouteBehaviorParams(
            EntityRepository repo,
            string? behaviorParams,
            out string translatedParams)
        {
            translatedParams = behaviorParams ?? string.Empty;

            if (string.IsNullOrWhiteSpace(behaviorParams))
                return true; // nothing to translate

            long routeEntityId;
            double speed = 0.0;
            bool loop   = false;

            try
            {
                using var doc  = JsonDocument.Parse(behaviorParams);
                var       root = doc.RootElement;

                if (!root.TryGetProperty("routeEntityId", out var routeEl))
                    return true; // not a network-ID-based FollowRoute task; pass through

                routeEntityId = routeEl.GetInt64();

                if (root.TryGetProperty("Speed", out var speedEl))
                    speedEl.TryGetDouble(out speed);

                if (root.TryGetProperty("Loop", out var loopEl))
                    loop = loopEl.GetBoolean();
            }
            catch
            {
                return true; // malformed JSON — let downstream handle it
            }

            // Find the route entity in ECS by NetworkIdentity.Value.
            var routeQuery = repo.Query()
                .With<NetworkIdentity>()
                .With<RouteTrajectoryCache>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            Entity found = Entity.Null;
            foreach (var e in routeQuery)
            {
                if (repo.GetComponent<NetworkIdentity>(e).Value == routeEntityId)
                {
                    found = e;
                    break;
                }
            }

            if (found == Entity.Null)
                return false; // entity not yet registered; retry

            var cache = repo.GetComponent<RouteTrajectoryCache>(found);
            if (cache.TrajectoryId == 0)
                return false; // route compiled but trajectory not yet ready; retry

            // Rewrite params with the resolved local trajectory ID.
            translatedParams =
                $"{{\"trajectoryId\":{cache.TrajectoryId}" +
                $",\"Speed\":{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $",\"Loop\":{(loop ? "true" : "false")}}}";
            return true;
        }

        private int ResolveDoctrineId(string? behaviorId)
        {
            if (string.IsNullOrWhiteSpace(behaviorId))
                return 0;

            if (_doctrineRegistry.TryGetId(behaviorId, out int doctrineId))
                return doctrineId;

            FdpLog<MissionControlRequestSystem>.Warn(
                "[MissionControl] Unknown BehaviorId '{0}'; using doctrine 0 (Idle).",
                behaviorId);
            return 0;
        }

        /// <summary>
        /// Delegates to <see cref="MissionTriggerHelper.ResolveTrigger"/> — shared implementation (BUG2-DEBT-01).
        /// </summary>
        internal static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<DdsMissionTrigger>? triggers)
            => MissionTriggerHelper.ResolveTrigger(triggers);

        // ── Test hooks ─────────────────────────────────────────────────────────

        /// <summary>Test hook: directly calls <see cref="ProcessRequest"/> bypassing DDS.</summary>
        internal void TestHook_ProcessRequest(EntityRepository repo, MissionControlRequest req)
            => ProcessRequest(repo, req);

        /// <summary>Test hook: number of requests currently in the retry queue.</summary>
        internal int TestHook_RetryQueueCount => _retryQueue.Count;

        /// <summary>Test hook: run one retry-drain cycle (processes a snapshot of the queue).</summary>
        internal void TestHook_DrainRetryQueue(EntityRepository repo)
        {
            int retryCount = _retryQueue.Count;
            for (int i = 0; i < retryCount; i++)
            {
                var (req, framesLeft) = _retryQueue.Dequeue();
                if (_entityMap.TryGetEntity(req.TargetEntityId, out _))
                {
                    ProcessRequest(repo, req);
                }
                else if (framesLeft > 0)
                {
                    _retryQueue.Enqueue((req, framesLeft - 1));
                }
                else
                {
                    WriteAck(req.RequestId, SstStatusCode.EntityNotFound, EntityNotFoundMessage, newVersion: 0);
                }
            }
        }
    }
}

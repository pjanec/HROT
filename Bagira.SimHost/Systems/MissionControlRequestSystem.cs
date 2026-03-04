using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Services;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Bagira.SimHost.Systems
{
    public class MissionControlRequestSystem : ComponentSystem
    {
        private const int ErrorCodeSuccess         = 0;
        private const int ErrorCodeEntityNotFound  = 2;
        private const int ErrorCodeNotSupported    = 6;
        private const int ErrorCodeVersionConflict = 7;

        private const string EntityNotFoundMessage  = "ERR_ENTITY_NOT_FOUND";
        private const string VersionConflictMessage = "ERR_VERSION_CONFLICT";

        private readonly DdsReader<MissionControlRequest> _reader;
        private readonly DdsWriter<MissionControlAck>     _writer;
        private readonly DdsWriter<EntityMission>         _missionStateWriter;
        private readonly NetworkEntityMap                 _entityMap;
        private readonly DoctrineRegistry                 _doctrineRegistry;
        private readonly Dictionary<long, long>           _missionVersions = new();
        private readonly Dictionary<long, List<Guid>>     _taskOrder = new();

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

        protected override void OnUpdate()
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                Console.WriteLine($"[MCRS-DBG] Received DDS command: {sample.Data.Payload._d} for entity {sample.Data.TargetEntityId}");
                ProcessRequest(World, sample.Data);
                Console.WriteLine($"[MCRS-DBG] After ProcessRequest done");
            }
        }

        private void ProcessRequest(EntityRepository repo, MissionControlRequest request)
        {
            if (!_entityMap.TryGetEntity(request.TargetEntityId, out var entity))
            {
                Console.WriteLine($"[MCRS-DBG] Entity {request.TargetEntityId} NOT in entityMap");
                WriteAck(request.RequestId, ErrorCodeEntityNotFound, EntityNotFoundMessage, newVersion: 0);
                return;
            }

            Console.WriteLine($"[MCRS-DBG] Entity {request.TargetEntityId} found in entityMap as entity {entity.Index}");

            long currentVersion = _missionVersions.TryGetValue(request.TargetEntityId, out var version)
                ? version
                : 0;

            switch (request.Payload._d)
            {
                case eMissionCommandType.CMD_REPLACE_MISSION:
                {
                    if (request.BaseVersion > 0 && request.BaseVersion != currentVersion)
                    {
                        WriteAck(request.RequestId, ErrorCodeVersionConflict, VersionConflictMessage, newVersion: 0);
                        return;
                    }

                    var plan = request.Payload.FullMissionData;
                    plan.Tasks ??= new List<MissionTask>();

                    var queue = BuildQueue(plan, out var orderedTaskIds);
                    if (repo.HasComponent<MissionPlanQueue>(entity))
                        repo.SetComponent(entity, queue);
                    else
                        repo.AddComponent(entity, queue);
                    _taskOrder[request.TargetEntityId] = orderedTaskIds;

                    Console.WriteLine($"[MCRS-DBG] SET MissionPlanQueue: PhaseCount={queue.PhaseCount}, CurrentPhase={queue.CurrentPhase} for entity {entity.Index}");

                    currentVersion++;
                    _missionVersions[request.TargetEntityId] = currentVersion;

                    WriteAck(request.RequestId, ErrorCodeSuccess, errorMessage: null, newVersion: currentVersion);
                    PublishEntityMission(request.TargetEntityId, plan);
                    return;
                }

                case eMissionCommandType.CMD_JUMP_TO_TASK:
                {
                    if (!_taskOrder.TryGetValue(request.TargetEntityId, out var orderedTaskIds))
                        orderedTaskIds = new List<Guid>();

                    int targetIndex = orderedTaskIds.IndexOf(request.Payload.TargetTaskId);
                    Console.WriteLine($"[MCRS-DBG] JUMP: taskOrderCount={orderedTaskIds.Count}, targetTaskId={request.Payload.TargetTaskId}, targetIndex={targetIndex}");
                    if (targetIndex < 0)
                        targetIndex = 0;

                    if (!repo.HasComponent<MissionPlanQueue>(entity))
                        repo.AddComponent(entity, new MissionPlanQueue());

                    ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
                    Console.WriteLine($"[MCRS-DBG] JUMP before: CurrentPhase={queue.CurrentPhase}, PhaseCount={queue.PhaseCount}");
                    queue.CurrentPhase = (byte)targetIndex;
                    queue.PhaseElapsedSeconds = 0f;
                    Console.WriteLine($"[MCRS-DBG] JUMP after: CurrentPhase={queue.CurrentPhase}, PhaseCount={queue.PhaseCount}");

                    currentVersion++;
                    _missionVersions[request.TargetEntityId] = currentVersion;

                    WriteAck(request.RequestId, ErrorCodeSuccess, errorMessage: null, newVersion: currentVersion);
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
                    if (repo.HasComponent<MissionPlanQueue>(entity))
                        repo.SetComponent(entity, abortQueue);
                    else
                        repo.AddComponent(entity, abortQueue);

                    _taskOrder[request.TargetEntityId] = new List<Guid>();

                    currentVersion++;
                    _missionVersions[request.TargetEntityId] = currentVersion;

                    WriteAck(request.RequestId, ErrorCodeSuccess, errorMessage: null, newVersion: currentVersion);
                    return;
                }

                default:
                    WriteAck(request.RequestId, ErrorCodeNotSupported, "ERR_NOT_SUPPORTED", newVersion: 0);
                    return;
            }
        }

        private void PublishEntityMission(long networkEntityId, MissionPlan plan)
        {
            _missionStateWriter.Write(new EntityMission
            {
                EntityId = networkEntityId,
                Plan     = plan,
            });
        }

        private void WriteAck(Guid requestId, int errorCode, string? errorMessage, long newVersion)
        {
            _writer.Write(new MissionControlAck
            {
                RequestId    = requestId,
                ErrorCode    = errorCode,
                ErrorMessage = errorMessage,
                NewVersion   = newVersion
            });
        }

        private MissionPlanQueue BuildQueue(MissionPlan plan, out List<Guid> orderedTaskIds)
        {
            orderedTaskIds = new List<Guid>();

            var queue = new MissionPlanQueue
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
            return queue;
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

        private static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<DdsMissionTrigger>? triggers)
        {
            if (triggers == null || triggers.Count == 0)
                return (EcsMissionTrigger.TimerElapsed, float.MaxValue); // no trigger = hold phase indefinitely

            var trigger = triggers[0];
            var type = trigger.Type ?? string.Empty;

            return type switch
            {
                "TimerElapsed"       => (EcsMissionTrigger.TimerElapsed, ParseTriggerParam(trigger.Params)),
                "ReachedDestination" => (EcsMissionTrigger.ReachedDestination, 0f),
                "HealthCritical"     => (EcsMissionTrigger.HealthCritical, ParseTriggerParam(trigger.Params)),
                _                    => (EcsMissionTrigger.TimerElapsed, 0f)
            };
        }

        private static float ParseTriggerParam(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0f;

            return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
        }
    }
}

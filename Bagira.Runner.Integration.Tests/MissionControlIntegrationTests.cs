using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.IOS.Services;
using Bagira.Map.Common;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;
using Xunit;
using DdsMissionTrigger = Bagira.BDC.SSTD.MissionTrigger;

namespace Bagira.Runner.Integration.Tests;

public class MissionControlIntegrationTests
{
    private const int MissionSeedTimeoutFrames = 100;
    private const int JumpTimeoutFrames = 100;
    private const int AckTimeoutFrames = 60;

    [Fact]
    public void IOS_SendsJumpCommand_SimHostAppliesIt()
    {
        using var harness = new BagiraRunnerHarness();

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, new GeoPosition
        {
            Latitude = 32.0853,
            Longitude = 34.7818,
            Altitude = 0
        });

        bool simHostReady = harness.PumpUntil(
            () => SimHostHasEntity(harness.SimHost.World, networkId),
            MissionSeedTimeoutFrames);
        Assert.True(simHostReady, "SimHost entity was not registered in time.");

        using var participant = new DdsParticipant((uint)harness.DomainId);
        using var requestWriter = new DdsWriter<MissionControlRequest>(participant, "MissionControlRequest");
        using var ackReader = new DdsReader<MissionControlAck>(participant, "MissionControlAck");

        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();
        var taskC = Guid.NewGuid();

        var plan = new MissionPlan
        {
            ActiveTaskId = taskA,
            Tasks = new List<MissionTask>
            {
                new MissionTask
                {
                    TaskId          = taskA,
                    BehaviorId      = "MoveToLocation",
                    BehaviorParams  = "{}",
                    ExecutingEngine = "CGFX",
                    State           = eTaskState.TASK_ACTIVE,
                    Triggers        = new List<DdsMissionTrigger>()
                },
                new MissionTask
                {
                    TaskId          = taskB,
                    BehaviorId      = "FollowRoute",
                    BehaviorParams  = "{}",
                    ExecutingEngine = "CGFX",
                    State           = eTaskState.TASK_PLANNED,
                    Triggers        = new List<DdsMissionTrigger>()
                },
                new MissionTask
                {
                    TaskId          = taskC,
                    BehaviorId      = "JoinFormation",
                    BehaviorParams  = "{}",
                    ExecutingEngine = "CGFX",
                    State           = eTaskState.TASK_PLANNED,
                    Triggers        = new List<DdsMissionTrigger>()
                }
            }
        };

        requestWriter.Write(new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = networkId,
            BaseVersion    = 0,
            Payload = new MissionCommandUnion
            {
                _d             = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = plan
            }
        });

        bool seeded = harness.PumpUntil(
            () => TryGetMissionQueue(harness.SimHost.World, networkId, out var queue)
               && queue.PhaseCount == 3
               && queue.CurrentPhase == 0,
            MissionSeedTimeoutFrames);
        Assert.True(seeded, "SimHost did not apply the mission plan in time.");

        var jumpRequestId = Guid.NewGuid();
        harness.Ios.Logic.TransactionManager.TrackRequest(jumpRequestId, "JumpToTask");

        requestWriter.Write(new MissionControlRequest
        {
            RequestId      = jumpRequestId,
            TargetEntityId = networkId,
            BaseVersion    = 0,
            Payload = new MissionCommandUnion
            {
                _d = eMissionCommandType.CMD_JUMP_TO_TASK,
                TargetTaskId = taskC
            }
        });

        bool jumped = harness.PumpUntil(
            () => TryGetMissionQueue(harness.SimHost.World, networkId, out var queue)
               && queue.CurrentPhase == 2,
            JumpTimeoutFrames);
        Assert.True(jumped, "SimHost did not jump to the target task in time.");

        MissionControlAck ack = default;
        bool ackObserved = harness.PumpUntil(
            () => TryTakeAck(ackReader, jumpRequestId, out ack),
            AckTimeoutFrames);
        Assert.True(ackObserved, "MissionControlAck did not arrive in time.");
        Assert.Equal(0, ack.ErrorCode);

        harness.Ios.Logic.TransactionManager.CompleteRequest(
            jumpRequestId, ack.ErrorCode == 0, ack.ErrorMessage);

        bool txCompleted = harness.PumpUntil(
            () => !IsRequestPending(harness.Ios.Logic.TransactionManager, jumpRequestId),
            AckTimeoutFrames);
        Assert.True(txCompleted, "IOS transaction did not complete in time.");
    }

    private static bool TryGetMissionQueue(EntityRepository world, long networkId, out MissionPlanQueue queue)
    {
        var view = (ISimulationView)world;
        var query = world.Query().With<NetworkIdentity>().With<MissionPlanQueue>().Build();
        foreach (var entity in query)
        {
            var id = view.GetComponentRO<NetworkIdentity>(entity);
            if (id.Value != networkId)
                continue;

            queue = view.GetComponentRO<MissionPlanQueue>(entity);
            return true;
        }

        queue = default;
        return false;
    }

    private static bool SimHostHasEntity(EntityRepository world, long networkId)
    {
        var view = (ISimulationView)world;
        var query = world.Query().With<NetworkIdentity>().Build();
        foreach (var entity in query)
        {
            var id = view.GetComponentRO<NetworkIdentity>(entity);
            if (id.Value == networkId)
                return true;
        }

        return false;
    }

    private static bool TryTakeAck(DdsReader<MissionControlAck> reader, Guid requestId, out MissionControlAck ack)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            if (sample.Data.RequestId != requestId) continue;

            ack = sample.Data;
            return true;
        }

        ack = default;
        return false;
    }

    private static bool IsRequestPending(IRequestTransactionManager txMgr, Guid requestId)
    {
        foreach (var pending in txMgr.GetPendingRequests())
        {
            if (pending.RequestId == requestId)
                return true;
        }

        return false;
    }
}

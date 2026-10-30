using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.Map.Common;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Xunit;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;

namespace Hrot.ClusterRunner.Integration.Tests;

public class MissionControlIntegrationTests
{
    private const int MissionSeedTimeoutFrames = 100;
    private const int JumpTimeoutFrames = 100;
    private const int AckTimeoutFrames = 60;

    // ⭐ R-3067 / AQ109 X1 — this test used to ALSO track the jump in ExCon's request-transaction manager and assert ExCon completed it; that half proved an
    //   ExCon-only capability (its own transaction read model) and is deleted with the node. What stays is the wire path it rode on: a MissionControlRequest on DDS
    //   is applied by the Brain (CGF), the jump moves the plan, and the ack comes back.
    [Fact]
    public void MissionControlRequest_JumpCommand_CgfAppliesItAndAcks()
    {
        using var harness = new HrotRunnerHarness();

        long tkbType = TkbEntityTypes.Tank_M1Abrams;
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, new CoreGeoPoint
        {
            Latitude = 32.0853,
            Longitude = 34.7818,
            Altitude = 0
        });

        bool simHostReady = harness.PumpUntil(
            () => SimHostHasEntity(harness.SimHost.World, networkId),
            MissionSeedTimeoutFrames);
        Assert.True(simHostReady, "SimHost entity was not registered in time.");

        // Wait for CGF to receive the ghost of the entity before sending the mission request.
        // MissionControlExecutionSystem only retries MaxEntityWaitFrames (10) before NACKing,
        // so the ghost must be present in CGF's entity map when the DDS message arrives.
        bool cgfReady = harness.PumpUntil(
            () => harness.Cgf!.GhostEntityMap != null
               && harness.Cgf!.GhostEntityMap.TryGetEntity(networkId, out _),
            MissionSeedTimeoutFrames);
        Assert.True(cgfReady, "CGF ghost entity was not created in time.");

        using var participant = new DdsParticipant((uint)harness.DomainId);
        using var requestWriter = new DdsWriter<MissionControlRequest>(participant);
        using var ackReader = new DdsReader<MissionControlAck>(participant);

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

        // MissionControlExecutionSystem runs on CGF (Brain tier) in the split architecture.
        // Check CGF's ECS world for the MissionPlanQueue, not SimHost's.
        bool seeded = harness.PumpUntil(
            () => TryGetMissionQueue(harness.Cgf!.World!, networkId, out var queue)
               && queue.PhaseCount == 3
               && queue.CurrentPhase == 0,
            MissionSeedTimeoutFrames);
        Assert.True(seeded, "CGF did not apply the mission plan in time.");

        var jumpRequestId = Guid.NewGuid();

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

        // MissionControlExecutionSystem runs on CGF (Brain tier) in the split architecture.
        bool jumped = harness.PumpUntil(
            () => TryGetMissionQueue(harness.Cgf!.World!, networkId, out var queue)
               && queue.CurrentPhase == 2,
            JumpTimeoutFrames);
        Assert.True(jumped, "CGF did not jump to the target task in time.");

        MissionControlAck ack = default;
        bool ackObserved = harness.PumpUntil(
            () => TryTakeAck(ackReader, jumpRequestId, out ack),
            AckTimeoutFrames);
        Assert.True(ackObserved, "MissionControlAck did not arrive in time.");
        Assert.Equal(0, ack.ErrorCode);
    }

    /// <summary>
    /// ⭐ R-3067 / AQ109 X1 — RE-HOMED from ExCon (Task 32B). The capability: a commit through the SHARED <see cref="Hrot.UI.Common.Facades.IMissionEditorService"/>
    /// resolves with the ack, not a timeout. It used to be driven through ExCon's own second service; it now runs on the CGF node (the Brain, where plans live)
    /// through the service its debug provider hands out — the HTTP/MCP mission-editing seam. 🔴 Mutation: build the service back inside CgfSubsystem's
    /// `!_headless` block ⇒ `MissionEditor` is null on this headless harness ⇒ red.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task Cgf_CommitMissionAsync_ResolvesWithAck_NotTimeout()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(TkbEntityTypes.Tank_M1Abrams, new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 });

        // The ghost must be on CGF first: MissionControlExecutionSystem waits only a few frames for the entity before it NACKs.
        Assert.True(harness.PumpUntil(
            () => harness.Cgf!.GhostEntityMap != null && harness.Cgf!.GhostEntityMap.TryGetEntity(networkId, out _), MissionSeedTimeoutFrames),
            "CGF ghost entity was not created in time.");

        var missions = harness.Cgf!.CreateDebugProvider()?.MissionEditor;
        Assert.NotNull(missions);

        var taskId = Guid.NewGuid();
        var plan = new Hrot.Core.Mission.MissionPlan
        {
            ActiveTaskId = taskId,
            Tasks = new List<Hrot.Core.Mission.MissionTask>
            {
                new Hrot.Core.Mission.MissionTask
                {
                    TaskId = taskId, ExecutingEngine = "CGFX", BehaviorId = "WanderMilitary", BehaviorParams = string.Empty,
                    State = Hrot.Core.Mission.eTaskState.TASK_PLANNED, Triggers = new List<Hrot.Core.Mission.MissionTrigger>(),
                }
            }
        };

        var commit = missions!.CommitMissionAsync(networkId, plan, 0);
        Assert.True(harness.PumpUntil(() => commit.IsCompleted, 200), "CommitMissionAsync did not complete — the ack never resolved the pending commit.");

        var result = await commit;
        Assert.True(result.Success, $"the commit completed but failed: {result.ErrorMessage}");
        Assert.True(result.NewVersion > 0, $"a successful commit returns NewVersion > 0, got {result.NewVersion}.");
    }

    private static bool TryGetMissionQueue(EntityRepository world, long networkId, out MissionPlanQueue queue)
    {
        var view = (ISimulationView)world;
        // Use EntityLifecycle.All so ghost entities in Constructing state are included.
        // CGF (Brain) does not run GhostPromotionSystem, so ghost entities never reach Alive.
        var query = world.Query().With<NetworkIdentity>().With<MissionPlanQueue>()
            .WithLifecycle(EntityLifecycle.All).Build();
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
}

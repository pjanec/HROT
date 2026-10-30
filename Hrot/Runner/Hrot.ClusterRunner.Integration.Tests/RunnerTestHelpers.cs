using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Shared helpers reused across runner integration test classes.
/// Extracted from the (now retired, R-3067) MapPlacement / AreaAuthoring / MiniExCon integration tests, and SpawnMovingVehicleWithGatewayIntegrationTests.
/// </summary>
internal static class RunnerTestHelpers
{
    /// <summary>
    /// ⭐ R-3067 / AQ109 X3 — what the IG's Mini ExCon panel's "spawn + wander" button did (<c>SubmitWithWanderMissionViaGateway</c>), as a plain
    /// call on a command gateway: create the entity at a LOCAL ENU position, await its allocated id, then replace its mission with a single
    /// <c>WanderMilitary</c> task. Returns the entity id (0 on a failed create).
    /// </summary>
    public static async System.Threading.Tasks.Task<long> SpawnWithWanderMissionAsync(
        Hrot.Core.Network.ICommandGateway gateway, long tkbType, int forceId, float x, float y)
    {
        int entityId = await gateway.CreateEntityAsync(new Hrot.Core.Network.CreateEntityCommand
        {
            TkbType = tkbType, X = x, Y = y, Z = 0.0, ForceId = forceId,
        });
        if (entityId <= 0) return 0L;

        var taskId = Guid.NewGuid();
        var plan = new Hrot.Core.Mission.MissionPlan
        {
            ActiveTaskId = taskId,
            Tasks = new System.Collections.Generic.List<Hrot.Core.Mission.MissionTask>
            {
                new Hrot.Core.Mission.MissionTask
                {
                    TaskId = taskId, ExecutingEngine = "CGFX", BehaviorId = "WanderMilitary", BehaviorParams = string.Empty,
                    Triggers = new System.Collections.Generic.List<Hrot.Core.Mission.MissionTrigger>(),   // single task: no advancement
                    State = Hrot.Core.Mission.eTaskState.TASK_PLANNED,
                }
            }
        };
        await gateway.SendMissionControlRequestAsync(new Hrot.Core.Network.MissionControlCommand
        {
            EntityId = entityId, CommandType = Hrot.Core.Mission.eMissionCommandType.CMD_REPLACE_MISSION, Plan = plan, BaseVersion = 0,
        });
        return entityId;
    }

    /// <summary>
    /// Attempts to take a <see cref="CreateUpdateDeleteEntityAck"/> from <paramref name="reader"/>
    /// that matches <paramref name="requestId"/>.
    ///
    /// <para>Returns <c>false</c> (letting <c>PumpUntil</c> retry) when:</para>
    /// <list type="bullet">
    ///   <item>No valid sample is available.</item>
    ///   <item>The available sample does not match <paramref name="requestId"/>.</item>
    ///   <item>The sample is a Phase-1 <see cref="NedStatusCode.InProgress"/> intermediate ACK —
    ///         callers must wait for the terminal Success / Error ACK.</item>
    /// </list>
    /// </summary>
    public static bool TryTakeCreateAck(
        DdsReader<CreateUpdateDeleteEntityAck> reader,
        Guid requestId,
        out CreateUpdateDeleteEntityAck ack)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var data = sample.Data;
            if (data.RequestId != requestId) continue;

            // Skip Phase-1 InProgress ACKs — let PumpUntil retry until the
            // terminal Success/Error ACK arrives.
            if (data.StatusCode == (int)NedStatusCode.InProgress)
            {
                ack = default;
                return false;
            }

            ack = data;
            return true;
        }

        ack = default;
        return false;
    }
}

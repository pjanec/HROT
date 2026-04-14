using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using CycloneDDS.Runtime;
using Hrot.Core.Network;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost;

/// <summary>
/// NED implementation of <see cref="ISimHostMissionSender"/>.
/// Sends a MoveToLocation doctrine mission via DDS <c>MissionControlRequest</c>.
/// </summary>
internal sealed class NedSimHostMissionSender : ISimHostMissionSender
{
    private readonly DdsWriter<MissionControlRequest> _writer;

    public NedSimHostMissionSender(DdsParticipant participant)
        => _writer = new DdsWriter<MissionControlRequest>(participant);

    public void SendNavigateToPoint(long entityNetworkId, Vector2 destination, float speed, float arrivalRadius)
    {
        var paramsJson = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"X\":{0},\"Y\":{1},\"Speed\":{2},\"ArrivalRadius\":{3}}}",
            destination.X, destination.Y, speed, arrivalRadius);

        var taskId = Guid.NewGuid();

        _writer.Write(new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = entityNetworkId,
            BaseVersion    = 0,
            Payload        = new MissionCommandUnion
            {
                _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan
                {
                    ActiveTaskId = taskId,
                    Tasks        = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = taskId,
                            ExecutingEngine = "CGFX",
                            BehaviorId      = "MoveToLocation",
                            BehaviorParams  = paramsJson,
                            Triggers        = new List<MissionTrigger>
                            {
                                new MissionTrigger { Type = "DoctrineFinished" },
                            },
                            State = eTaskState.TASK_PLANNED,
                        }
                    },
                },
            },
        });
    }

    public void Dispose() => _writer.Dispose();
}

using System.Linq;
using CoreMission = Hrot.Core.Mission;
using NedMission  = Hrot.NED.Descriptors;

namespace Hrot.Network.NED.ExCon;

/// <summary>
/// Converts between <see cref="CoreMission.MissionPlan"/> and
/// <see cref="NedMission.MissionPlan"/> wire types.
/// </summary>
internal static class NedMissionHelper
{
    /// <summary>Converts a neutral <see cref="CoreMission.MissionPlan"/> to its NED wire form.</summary>
    public static NedMission.MissionPlan ToNed(CoreMission.MissionPlan p)
        => new NedMission.MissionPlan
        {
            ActiveTaskId = p.ActiveTaskId,
            Tasks        = p.Tasks?.Select(ToNed).ToList()
                           ?? new System.Collections.Generic.List<NedMission.MissionTask>(),
        };

    /// <summary>Converts a NED wire <see cref="NedMission.MissionPlan"/> to its neutral form.</summary>
    public static CoreMission.MissionPlan ToNeutral(NedMission.MissionPlan p)
        => new CoreMission.MissionPlan
        {
            ActiveTaskId = p.ActiveTaskId,
            Tasks        = p.Tasks?.Select(ToNeutral).ToList()
                           ?? new System.Collections.Generic.List<CoreMission.MissionTask>(),
        };

    private static NedMission.MissionTask ToNed(CoreMission.MissionTask t)
        => new NedMission.MissionTask
        {
            TaskId          = t.TaskId,
            ExecutingEngine = t.ExecutingEngine,
            BehaviorId      = t.BehaviorId,
            BehaviorParams  = t.BehaviorParams,
            Triggers        = t.Triggers?.Select(x => new NedMission.MissionTrigger
                              { Type = x.Type, Params = x.Params }).ToList()
                              ?? new System.Collections.Generic.List<NedMission.MissionTrigger>(),
            State           = (NedMission.eTaskState)(int)t.State,
        };

    private static CoreMission.MissionTask ToNeutral(NedMission.MissionTask t)
        => new CoreMission.MissionTask
        {
            TaskId          = t.TaskId,
            ExecutingEngine = t.ExecutingEngine,
            BehaviorId      = t.BehaviorId,
            BehaviorParams  = t.BehaviorParams,
            Triggers        = t.Triggers?.Select(x => new CoreMission.MissionTrigger
                              { Type = x.Type, Params = x.Params }).ToList()
                              ?? new System.Collections.Generic.List<CoreMission.MissionTrigger>(),
            State           = (CoreMission.eTaskState)(int)t.State,
        };
}

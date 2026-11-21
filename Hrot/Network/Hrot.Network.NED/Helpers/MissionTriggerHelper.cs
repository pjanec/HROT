using System.Collections.Generic;
using Hrot.NED.Descriptors;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;
using EcsMissionTrigger = Fdp.Toolkit.Behavior.Components.MissionTrigger;

namespace Hrot.Map.Common.Helpers
{
    /// <summary>
    /// ⭐ <c>CE-4157</c> — a THIN adapter: the DDS trigger list mapped onto the core parser (<c>Hrot.Core.Mission.MissionTriggerHelper</c> — the one
    /// switch; there is no second copy). Throws <c>Hrot.Core.Mission.MissionPlanException</c> naming the task.
    /// </summary>
    public static class MissionTriggerHelper
    {
        public static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<DdsMissionTrigger>? triggers, string? task)
            => Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger(
                triggers?.ConvertAll(t => new Hrot.Core.Mission.MissionTrigger { Type = t.Type ?? string.Empty, Params = t.Params ?? string.Empty }), task);
    }
}

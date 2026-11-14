using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2135</c> P4 (R-2004 — <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10 P4-g) — <c>Demo_MissionPlan</c>'s params DTO:
    /// the blueprint names it and declares no Parameters; its fields ARE the Parameters (the Behaviour Task nodes run its <c>Move</c>,
    /// <c>Fire</c> and <c>Home</c> as their children's params). The JSON is unchanged: <c>{"Move":{…},"Fire":{…},"Home":{…}}</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct DemoMissionPlanParams
    {
        /// <summary>The advance — <c>Demo_Advance</c>'s MoveToLocation.</summary>
        public MoveToLocationParamsJsonDto Move;
        /// <summary>The defence — <c>Demo_Engage</c>'s FireAtTarget.</summary>
        public FireAtTargetParamsJsonDto Fire;
        /// <summary>The return — <c>Demo_Retreat</c>'s MoveToLocation.</summary>
        public MoveToLocationParamsJsonDto Home;
    }

    /// <summary>⭐ <c>CE-2135</c> P4 — <c>Demo_TaskChain</c>'s params DTO (see <see cref="DemoMissionPlanParams"/>):
    /// <c>{"Move":{…},"Fire":{…}}</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct DemoTaskChainParams
    {
        /// <summary>The advance — <c>Demo_Advance</c>'s MoveToLocation.</summary>
        public MoveToLocationParamsJsonDto Move;
        /// <summary>The engagement — <c>Demo_Engage</c>'s FireAtTarget.</summary>
        public FireAtTargetParamsJsonDto Fire;
    }
}

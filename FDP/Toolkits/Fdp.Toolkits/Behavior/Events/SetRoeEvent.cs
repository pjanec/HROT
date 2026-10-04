using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// ⭐ <c>CE-2074</c> (R-200) — an order changes a unit's ROE. Applied by <c>RoeSystem</c>, gated by origin exactly as
    /// a behaviour is (a lower origin than the one that set the ROE is refused). A field left <c>Unset</c> keeps its value.
    /// </summary>
    [EventId(BehaviorConstants.EventId_SetRoe)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SetRoeEvent
    {
        /// <summary>The unit.</summary>
        public Entity Entity;
        /// <summary>The new fire rule; <see cref="RoeFire.FireUnset"/> keeps the current one.</summary>
        public RoeFire Fire;
        /// <summary>The new reaction rule; <see cref="RoeReactions.ReactionsUnset"/> keeps the current one.</summary>
        public RoeReactions Reactions;
        /// <summary>Who orders it; unmarked reads as Operator.</summary>
        public BehaviorOrigin Origin;
    }
}

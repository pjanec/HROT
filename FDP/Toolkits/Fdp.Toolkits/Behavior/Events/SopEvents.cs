using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>⭐ <c>CE-3035</c> — assign a unit's SOP (R-189). Gated against <see cref="SopState.SopOrigin"/> like a behaviour.
    /// Refused when it names the unit's current task, or a behaviour that drives channels.</summary>
    public sealed class AssignSopEvent
    {
        /// <summary>The unit.</summary>
        public Entity Entity;
        /// <summary>The SOP behaviour.</summary>
        public string BehaviorName = string.Empty;
        /// <summary>Its params as JSON (R-191).</summary>
        public string JsonParams = string.Empty;
        /// <summary>Who asks; unmarked reads as Operator.</summary>
        public BehaviorOrigin Origin;
    }

    /// <summary>⭐ <c>CE-3035</c> — remove a unit's SOP (the unit then does only what it is told). Gated like an assign.</summary>
    [EventId(BehaviorConstants.EventId_ClearSop)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ClearSopEvent
    {
        /// <summary>The unit.</summary>
        public Entity Entity;
        /// <summary>Who asks; unmarked reads as Operator.</summary>
        public BehaviorOrigin Origin;
    }
}

using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Managed event that requests assignment of a new behavior to an entity.
    /// Published via <c>World.Bus.PublishManaged&lt;AssignBehaviorEvent&gt;(evt)</c> and
    /// consumed synchronously by <see cref="Systems.BehaviorIngressSystem"/> within the
    /// same frame (after <c>World.Bus.SwapBuffers()</c>).
    ///
    /// Must be a class (not a struct) because it carries managed string fields.
    /// </summary>
    public sealed class AssignBehaviorEvent
    {
        /// <summary>The entity to assign the behavior to.</summary>
        public Entity Entity;

        /// <summary>
        /// Name of the behavior to assign, e.g. <c>"FleeToSafety"</c>.
        /// Must match a name registered in <see cref="BehaviorRegistry"/>.
        /// </summary>
        public string BehaviorName = string.Empty;

        /// <summary>
        /// Serialised JSON parameters for the behavior's blackboard, e.g. <c>"50.0"</c>.
        /// Passed verbatim to <see cref="BehaviorDefinition.ParseParams"/>.
        /// Empty string is valid when the behavior has no configurable parameters.
        /// </summary>
        public string JsonParams = string.Empty;
    }
}

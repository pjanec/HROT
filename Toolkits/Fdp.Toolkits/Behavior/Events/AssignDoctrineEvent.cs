using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Managed event that requests assignment of a new doctrine to an entity.
    /// Published via <c>World.Bus.PublishManaged&lt;AssignDoctrineEvent&gt;(evt)</c> and
    /// consumed synchronously by <see cref="Systems.DoctrineIngressSystem"/> within the
    /// same frame (after <c>World.Bus.SwapBuffers()</c>).
    ///
    /// Must be a class (not a struct) because it carries managed string fields.
    /// </summary>
    public sealed class AssignDoctrineEvent
    {
        /// <summary>The entity to assign the doctrine to.</summary>
        public Entity Entity;

        /// <summary>
        /// Name of the doctrine to assign, e.g. <c>"FleeToSafety"</c>.
        /// Must match a name registered in <see cref="DoctrineRegistry"/>.
        /// </summary>
        public string DoctrineName = string.Empty;

        /// <summary>
        /// Serialised JSON parameters for the doctrine's blackboard, e.g. <c>"50.0"</c>.
        /// Passed verbatim to <see cref="DoctrineDefinition.ParseParams"/>.
        /// Empty string is valid when the doctrine has no configurable parameters.
        /// </summary>
        public string JsonParams = string.Empty;
    }
}

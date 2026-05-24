using System;

namespace Fbt
{
    /// <summary>
    /// Marks a static method as the deactivation companion for a BTree action.
    /// The BTree interpreter invokes the deactivator when the execution pointer
    /// leaves the paired action for any reason (natural completion, abort, branch switch).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeDeactivatorAttribute : Attribute
    {
        /// <summary>
        /// Fully qualified method name of the paired action, matching the registration
        /// key used in <see cref="Fbt.Runtime.ActionRegistry{TBlackboard,TContext}.Register"/>.
        /// </summary>
        public string TargetAction { get; }

        public BTreeDeactivatorAttribute(string targetAction)
        {
            TargetAction = targetAction;
        }
    }
}

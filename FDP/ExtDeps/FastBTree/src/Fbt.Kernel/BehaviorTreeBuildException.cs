using System;

namespace Fbt
{
    /// <summary>
    /// Thrown when a behavior tree cannot be compiled due to structural or validation errors.
    /// </summary>
    public class BehaviorTreeBuildException : Exception
    {
        public BehaviorTreeBuildException(string message) : base(message) { }
        public BehaviorTreeBuildException(string message, Exception innerException) : base(message, innerException) { }
    }
}

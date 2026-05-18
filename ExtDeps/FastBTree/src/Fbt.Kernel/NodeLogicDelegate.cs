namespace Fbt
{
    /// <summary>
    /// Delegate for a node's execution logic.
    /// </summary>
    /// <typeparam name="TBlackboard">The blackboard type.</typeparam>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="blackboard">Reference to blackboard.</param>
    /// <param name="state">Reference to tree state.</param>
    /// <param name="context">Reference to execution context.</param>
    /// <param name="paramIndex">Index for looking up parameters.</param>
    /// <returns>Execution status.</returns>
    public delegate NodeStatus NodeLogicDelegate<TBlackboard, TContext>(
        ref TBlackboard blackboard,
        ref BehaviorTreeState state,
        ref TContext context,
        int paramIndex)
        where TBlackboard : struct
        where TContext : struct, IAIContext;
}

namespace Fbt
{
    /// <summary>
    /// Delegate for a node's deactivation cleanup logic.
    /// Mirrors <see cref="NodeLogicDelegate{TBlackboard,TContext}"/> but returns void.
    /// Invoked by the interpreter when the execution pointer leaves an action or condition
    /// annotated with <see cref="BTreeDeactivatorAttribute"/>.
    /// </summary>
    /// <typeparam name="TBlackboard">The blackboard type.</typeparam>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="blackboard">Reference to blackboard.</param>
    /// <param name="state">Reference to tree state.</param>
    /// <param name="context">Reference to execution context.</param>
    /// <param name="paramIndex">Payload index of the node being deactivated.</param>
    public delegate void NodeDeactivatorDelegate<TBlackboard, TContext>(
        ref TBlackboard blackboard,
        ref BehaviorTreeState state,
        ref TContext context,
        int paramIndex)
        where TBlackboard : struct
        where TContext : struct, IAIContext;
}

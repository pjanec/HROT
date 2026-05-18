using Fbt;

namespace Fbt.Compiler
{
    /// <summary>
    /// Condition delegate for expression-based blackboard parameter binding.
    /// Receives a reference to a projected sub-field of the blackboard instead of
    /// the full blackboard, enabling reusable delegates across different blackboard types.
    /// </summary>
    /// <typeparam name="TValue">Blackboard sub-field type. Must be unmanaged.</typeparam>
    /// <typeparam name="TContext">Context struct type.</typeparam>
    public delegate NodeStatus ReusableConditionDelegate<TValue, TContext>(
        ref TValue data, ref BehaviorTreeState state, ref TContext ctx)
        where TValue : unmanaged
        where TContext : struct, IAIContext;

    /// <summary>
    /// Action delegate for expression-based blackboard parameter binding.
    /// Receives a reference to a projected sub-field of the blackboard instead of
    /// the full blackboard, enabling reusable delegates across different blackboard types.
    /// </summary>
    /// <typeparam name="TValue">Blackboard sub-field type. Must be unmanaged.</typeparam>
    /// <typeparam name="TContext">Context struct type.</typeparam>
    public delegate NodeStatus ReusableActionDelegate<TValue, TContext>(
        ref TValue data, ref BehaviorTreeState state, ref TContext ctx)
        where TValue : unmanaged
        where TContext : struct, IAIContext;
}

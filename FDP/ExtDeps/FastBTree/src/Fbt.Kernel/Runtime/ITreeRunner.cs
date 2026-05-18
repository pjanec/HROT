namespace Fbt.Runtime
{
    /// <summary>
    /// Interface for a behavior tree execution engine.
    /// </summary>
    public interface ITreeRunner<TBlackboard, TContext>
        where TBlackboard : struct
        where TContext : struct, IAIContext
    {
        /// <summary>
        /// Execute the tree logic for one tick.
        /// </summary>
        NodeStatus Tick(
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context);
    }
}

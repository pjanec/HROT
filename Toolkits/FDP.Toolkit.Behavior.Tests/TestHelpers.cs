using Fdp.Kernel;
using FDP.Toolkit.Behavior.Executors;

namespace FDP.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Reusable spy executor that records how many times each lifecycle method was called.
    /// Use in dispatcher tests to verify OnEnter/Execute/OnExit call counts.
    /// </summary>
    public class SpyExecutor<TChannel> : IActionExecutor<TChannel>
        where TChannel : struct
    {
        public int OnEnterCallCount { get; private set; }
        public int ExecuteCallCount { get; private set; }
        public int OnExitCallCount { get; private set; }

        public void OnEnter(Entity entity, ref TChannel channel, EntityRepository world)
            => OnEnterCallCount++;

        public void Execute(Entity entity, ref TChannel channel, EntityRepository world, float dt)
            => ExecuteCallCount++;

        public void OnExit(Entity entity, ref TChannel channel, EntityRepository world)
            => OnExitCallCount++;
    }
}

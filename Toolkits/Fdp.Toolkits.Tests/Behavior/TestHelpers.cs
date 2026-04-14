using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Behavior.Tests
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

    /// <summary>
    /// Spy executor that also writes <see cref="NodeStatus.Running"/> into the channel on
    /// <see cref="Execute"/>, verifying the direct-write status contract described in
    /// <see cref="IActionExecutor{TChannel}.Execute"/>.
    /// All three channel types share identical layout so the reinterpret cast is safe.
    /// </summary>
    public class WritingSpyExecutor<TChannel> : IActionExecutor<TChannel>
        where TChannel : struct
    {
        public int OnEnterCallCount { get; private set; }
        public int ExecuteCallCount { get; private set; }
        public int OnExitCallCount { get; private set; }

        public void OnEnter(Entity entity, ref TChannel channel, EntityRepository world)
            => OnEnterCallCount++;

        public void Execute(Entity entity, ref TChannel channel, EntityRepository world, float dt)
        {
            ExecuteCallCount++;
            // All channel types (LocomotionChannel / WeaponChannel / InteractionChannel) are
            // layout-identical, so this reinterpret is safe. See BCS-P1-T1 layout guarantee.
            ref var asLoco = ref Unsafe.As<TChannel, LocomotionChannel>(ref channel);
            asLoco.Status = NodeStatus.Running;
        }

        public void OnExit(Entity entity, ref TChannel channel, EntityRepository world)
            => OnExitCallCount++;
    }
}

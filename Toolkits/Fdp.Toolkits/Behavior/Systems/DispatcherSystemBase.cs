using System;
using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Shared registration and previous-action tracking for all dispatcher systems.
    /// Each concrete dispatcher implements OnUpdate with its own channel type and capability check.
    /// </summary>
    public abstract class DispatcherSystemBase<TChannel> : ComponentSystem
        where TChannel : struct
    {
        private const int InitialPreviousActionCapacity = 256;

        protected readonly IActionExecutor<TChannel>[] _executors =
            new IActionExecutor<TChannel>[BehaviorConstants.MaxActionTypes];

        protected ushort[] _previousAction = Array.Empty<ushort>();

        protected override void OnCreate()
        {
            _previousAction = new ushort[InitialPreviousActionCapacity];
        }

        /// <summary>Register an executor to handle a specific action kind.</summary>
        public void RegisterExecutor(ushort actionId, IActionExecutor<TChannel> executor)
        {
            _executors[actionId] = executor;
        }

        /// <summary>Grow _previousAction if entity.Index exceeds current capacity.</summary>
        protected void EnsurePreviousActionCapacity(int requiredMinSize)
        {
            if (_previousAction.Length < requiredMinSize)
            {
                int newSize = Math.Max(_previousAction.Length * 2, requiredMinSize);
                Array.Resize(ref _previousAction, newSize);
            }
        }
    }
}

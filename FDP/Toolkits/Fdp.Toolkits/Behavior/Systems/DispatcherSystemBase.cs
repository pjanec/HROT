using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Shared executor registration for all dispatcher systems.
    /// Each concrete dispatcher implements OnUpdate with its own channel type and capability check.
    /// ⭐ <c>R-3039</c> S1 — the action each entity last entered is on its channel (<c>DispatchedAction</c>, beside
    /// <c>DispatchedInstanceId</c>), never an array here: a checkpoint restored into a fresh process still owes the right OnExit, and an
    /// entity index reused by the next world starts from its own fresh channel (what <c>ForgetLastWorld</c> used to do, CE-3076).
    /// </summary>
    public abstract class DispatcherSystemBase<TChannel> : IEcsModuleSystem
        where TChannel : struct
    {
        protected readonly IActionExecutor<TChannel>[] _executors =
            new IActionExecutor<TChannel>[BehaviorConstants.MaxActionTypes];

        /// <summary>Register an executor to handle a specific action kind.</summary>
        public void RegisterExecutor(ushort actionId, IActionExecutor<TChannel> executor)
        {
            _executors[actionId] = executor;
        }

        /// <inheritdoc/>
        public abstract void Execute(ISimulationView view, float deltaTime);
    }
}

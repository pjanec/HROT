using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Shared registration and previous-action tracking for all dispatcher systems.
    /// Each concrete dispatcher implements OnUpdate with its own channel type and capability check.
    /// </summary>
    public abstract class DispatcherSystemBase<TChannel> : IEcsModuleSystem
        where TChannel : struct
    {
        private const int InitialPreviousActionCapacity = 256;

        protected readonly IActionExecutor<TChannel>[] _executors =
            new IActionExecutor<TChannel>[BehaviorConstants.MaxActionTypes];

        protected ushort[] _previousAction = new ushort[InitialPreviousActionCapacity];

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

        private int _worldEpoch;

        /// <summary>
        /// ⭐ <c>CE-3076</c> (<c>CE-2101</c>'s rule, <c>DESIGN_Cluster_Load_Phase.md</c> §8) — <see cref="_previousAction"/> is indexed
        /// by <c>entity.Index</c>, which the world boundary REUSES (every entity is destroyed; the next world's land on the freed
        /// indices). ⛔ Without this the first dispatch of a new entity ran the LAST world's action's <c>OnExit</c> on it. Call
        /// first in <see cref="Execute"/>.
        /// </summary>
        protected void ForgetLastWorld(ISimulationView view)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch))
                Array.Clear(_previousAction);
        }

        /// <inheritdoc/>
        public abstract void Execute(ISimulationView view, float deltaTime);
    }
}

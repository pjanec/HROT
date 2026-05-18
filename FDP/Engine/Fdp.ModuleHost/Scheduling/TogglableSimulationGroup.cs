using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Togglable wrapper for simulation-phase systems.
    /// Implements <see cref="ISystemGroup"/> so <c>SystemScheduler</c> can profile
    /// each inner system individually.
    /// When <see cref="Enabled"/> is false the inner systems are not executed.
    /// The replay handler flips this flag during PrepareReplay / FinalizeReplay / PrepareLive.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class TogglableSimulationGroup : ISystemGroup
    {
        private readonly IEcsModuleSystem[] _innerSystems;

        /// <inheritdoc />
        public bool Enabled { get; set; } = true;

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>
        /// Creates a group with the given inner systems.
        /// </summary>
        /// <param name="name">Name of this group (for profiling/debugging).</param>
        /// <param name="innerSystems">
        /// The ordered list of <see cref="IEcsModuleSystem"/> instances to execute
        /// when the group is enabled.
        /// </param>
        public TogglableSimulationGroup(string name, params IEcsModuleSystem[] innerSystems)
        {
            Name = name;
            _innerSystems = innerSystems ?? System.Array.Empty<IEcsModuleSystem>();
        }

        /// <summary>
        /// Creates a group with the given inner systems.
        /// </summary>
        /// <param name="name">Name of this group (for profiling/debugging).</param>
        /// <param name="innerSystems">
        /// The ordered list of <see cref="IEcsModuleSystem"/> instances to execute
        /// when the group is enabled.
        /// </param>
        public TogglableSimulationGroup(string name, IReadOnlyList<IEcsModuleSystem> innerSystems)
        {
            Name = name;
            if (innerSystems == null)
            {
                _innerSystems = System.Array.Empty<IEcsModuleSystem>();
                return;
            }
            _innerSystems = new IEcsModuleSystem[innerSystems.Count];
            for (int i = 0; i < innerSystems.Count; i++)
                _innerSystems[i] = innerSystems[i];
        }

        /// <inheritdoc />
        public IReadOnlyList<IEcsModuleSystem> GetSystems() => _innerSystems;

        /// <summary>
        /// Executes all inner systems in order.  Does nothing when
        /// <see cref="Enabled"/> is <c>false</c>.
        /// </summary>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (!Enabled) return;
            foreach (var sys in _innerSystems)
                sys.Execute(view, deltaTime);
        }
    }
}

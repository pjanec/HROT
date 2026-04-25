using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Togglable wrapper for post-simulation physics integration systems.
    /// Implements <see cref="ISystemGroup"/> so <c>SystemScheduler</c> can profile
    /// each inner system individually.
    /// Must be disabled during replay to prevent kinematic integration from
    /// overwriting restored historical <c>SimTransform</c> positions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// During replay, <c>PlaybackTickSystem</c> restores historical ECS state
    /// (positions, velocities) from the recording each frame.  If physics integration
    /// systems such as <c>BallisticsSystem</c>, <c>LinearKinematicsSystem</c>, or
    /// <c>CarKinematicsSystem</c> run afterwards in the <c>PostSimulation</c> phase
    /// they will integrate velocity into <c>SimTransform.Position</c> again, advancing
    /// positions past the recorded values and corrupting the replay.
    /// Setting <see cref="Enabled"/> to <c>false</c> during replay prevents this.
    /// <c>PlaybackTickSystem</c> itself must NOT be placed inside this group — it must
    /// always run and is registered directly with the kernel by <c>ReplayModule</c>.
    /// </para>
    /// </remarks>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class TogglablePostSimulationGroup : ISystemGroup
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
        /// when the group is enabled.  Typically physics integration systems.
        /// </param>
        public TogglablePostSimulationGroup(string name, params IEcsModuleSystem[] innerSystems)
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
        /// when the group is enabled.  Typically physics integration systems.
        /// </param>
        public TogglablePostSimulationGroup(string name, IReadOnlyList<IEcsModuleSystem> innerSystems)
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

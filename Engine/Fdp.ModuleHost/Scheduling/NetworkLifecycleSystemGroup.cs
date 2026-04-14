using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Groups the three network lifecycle systems — <c>LifecycleSystem</c>,
    /// <c>GhostPromotionSystem</c>, and <c>NetworkGatewaySystem</c> — under a
    /// single gate.
    ///
    /// <para>
    /// When <see cref="Enabled"/> is <c>true</c> (default), all three inner
    /// systems execute normally each frame.  When set to <c>false</c>
    /// (e.g. during <c>RunningReplay</c> by <c>ReplayLoadClusterStateHandler</c>),
    /// <see cref="ExecuteGroup"/> iterates zero systems so no lifecycle state
    /// changes or ghost promotions occur during playback (CGF1-S0304).
    /// Reset to <c>true</c> by <c>ReplayLoadClusterStateHandler</c> when returning to
    /// <c>RunningLive</c> (CGF1-S0305).
    /// </para>
    /// </summary>
    public sealed class NetworkLifecycleSystemGroup
    {
        private readonly IEcsModuleSystem[] _innerSystems;

        /// <summary>
        /// When <c>false</c>, <see cref="ExecuteGroup"/> is a no-op — none of
        /// the inner systems' <c>Execute</c> methods are called.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Creates a group with the given inner systems.
        /// </summary>
        /// <param name="innerSystems">
        /// The ordered list of <see cref="IEcsModuleSystem"/> instances to execute
        /// when the group is enabled.  Typically: <c>LifecycleSystem</c>,
        /// <c>GhostPromotionSystem</c>, <c>NetworkGatewaySystem</c>.
        /// </param>
        public NetworkLifecycleSystemGroup(params IEcsModuleSystem[] innerSystems)
        {
            _innerSystems = innerSystems ?? System.Array.Empty<IEcsModuleSystem>();
        }

        /// <summary>
        /// Executes all inner systems in order, passing <paramref name="view"/>
        /// and <paramref name="deltaTime"/> to each.  Does nothing when
        /// <see cref="Enabled"/> is <c>false</c>.
        /// </summary>
        public void ExecuteGroup(ISimulationView view, float deltaTime)
        {
            if (!Enabled) return;
            foreach (var sys in _innerSystems)
                sys.Execute(view, deltaTime);
        }
    }
}

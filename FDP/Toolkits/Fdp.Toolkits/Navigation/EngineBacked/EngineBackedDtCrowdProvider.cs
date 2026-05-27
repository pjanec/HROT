using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// No-op crowd provider stub for engine-backed scenarios.
    /// All methods are safe no-ops; <c>GetAgentVelocity</c> always returns Zero.
    /// Humanoid navigation in this mode is handled by <c>LinearKinematicsSystem</c>.
    /// </summary>
    public sealed class EngineBackedDtCrowdProvider : IDtCrowdProvider
    {
        /// <inheritdoc/>
        public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters) => true;

        /// <inheritdoc/>
        public void UnregisterAgent(Entity entity) { }

        /// <inheritdoc/>
        public void SetAgentTarget(Entity entity, Vector3 target) { }

        /// <inheritdoc/>
        public void Update(float dt, ISimulationView view) { }

        /// <inheritdoc/>
        public Vector3 GetAgentVelocity(Entity entity) => Vector3.Zero;

        /// <inheritdoc/>
        public bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }
}

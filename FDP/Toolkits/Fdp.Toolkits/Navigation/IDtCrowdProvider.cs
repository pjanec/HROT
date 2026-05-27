using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Crowd steering provider. Implemented by <c>FakeDtCrowdProvider</c> for tests and
    /// eventually by a DotRecast/dtCrowd port for production.
    /// </summary>
    public interface IDtCrowdProvider
    {
        /// <summary>Add an agent. Returns false if the entity is already registered.</summary>
        bool RegisterAgent(Entity entity, in CrowdAgentParams parameters);

        /// <summary>Remove an agent. Safe to call if not registered.</summary>
        void UnregisterAgent(Entity entity);

        /// <summary>Update the agent's steering target. Idempotent within a tick.</summary>
        void SetAgentTarget(Entity entity, Vector3 target);

        /// <summary>
        /// Advance the crowd simulation by <paramref name="dt"/> seconds.
        /// Reads <see cref="SimTransform"/> from <paramref name="view"/> for each agent;
        /// writes per-agent velocity outputs via <see cref="GetAgentVelocity"/>.
        /// </summary>
        void Update(float dt, ISimulationView view);

        /// <summary>Get the crowd-computed velocity for an agent (set by last <see cref="Update"/>).</summary>
        Vector3 GetAgentVelocity(Entity entity);

        /// <summary>Read current agent state. Returns false if entity is not registered.</summary>
        bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot);
    }

    /// <summary>
    /// Parameters used when registering an agent with <see cref="IDtCrowdProvider"/>.
    /// </summary>
    public struct CrowdAgentParams
    {
        /// <summary>Agent collision radius in metres (typically VehicleParams.Width * 0.5).</summary>
        public float Radius;

        /// <summary>Agent standing height in metres.</summary>
        public float Height;

        /// <summary>Maximum speed in m/s.</summary>
        public float MaxSpeed;

        /// <summary>Maximum acceleration in m/s^2.</summary>
        public float MaxAcceleration;

        /// <summary>Separation preference weight. Default 2. Higher = stronger separation.</summary>
        public byte SeparationWeight;
    }

    /// <summary>
    /// Read-only snapshot of an agent's current crowd-internal state (for diagnostics).
    /// </summary>
    public struct CrowdAgentSnapshot
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Target;
        public Vector3 DesiredVelocity;
        public bool    ReachedTarget;
        public int     NearbyAgentCount;
    }
}

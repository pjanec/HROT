using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Test-API surface for direct inspection of <see cref="FakeDtCrowdProvider"/>.
    /// </summary>
    public interface IFakeDtCrowdProviderTestApi
    {
        /// <summary>Returns the list of registered entity indices in insertion order.</summary>
        IReadOnlyList<int> RegisteredEntityIndices { get; }

        /// <summary>Returns the total number of <see cref="IDtCrowdProvider.Update"/> calls made.</summary>
        int UpdateCallCount { get; }

        /// <summary>Force an agent's computed velocity, bypassing the steering output.</summary>
        void OverrideAgentVelocity(Entity entity, Vector3 velocity);

        /// <summary>Clear a velocity override set by <see cref="OverrideAgentVelocity"/>.</summary>
        void ClearAgentVelocityOverride(Entity entity);
    }

    /// <summary>
    /// Fake crowd steering provider for unit tests.
    ///
    /// Algorithm (O(N^2), N = registered agents):
    ///   1. Compute desired velocity for each agent toward its target (clamped to MaxSpeed).
    ///   2. Apply simple separation: subtract a fraction of the overlap impulse from every
    ///      neighbouring pair whose separation is less than the sum of their radii.
    ///   3. Apply acceleration clamping: final velocity change per tick is bounded by
    ///      MaxAcceleration * dt.
    ///
    /// Processing order is deterministic (sorted by entity Index, ascending).
    /// Separation avoids NaN via <c>SafeNormalize</c> (zero vector stays zero).
    /// </summary>
    public sealed class FakeDtCrowdProvider : IDtCrowdProvider, IFakeDtCrowdProviderTestApi
    {
        // ── Separation constants (DD-Fake-Nav §4.3) ──────────────────────────────
        // Separation force is applied when dist < combinedR * SeparationRadiusMultiplier.
        // NearbyAgentCount is incremented when dist < combinedR * NearbyAgentRadiusMultiplier.
        private const float SeparationRadiusMultiplier  = 1.5f;
        private const float NearbyAgentRadiusMultiplier = 4.0f;
        // Minimum distance used in the push formula denominator to avoid division by zero.
        private const float SeparationMinDist = 0.01f;
        private sealed class AgentEntry
        {
            public Entity              Entity;
            public CrowdAgentParams    Params;
            public Vector3             Target;
            public Vector3             Velocity;
            public bool                HasTarget;
            public bool                ReachedTarget;
            public int                 NearbyAgentCount;
            public bool                HasVelocityOverride;
            public Vector3             OverriddenVelocity;
        }

        // Ordered by insertion index for snapshot; sorted by entity index for Update.
        private readonly Dictionary<int, AgentEntry> _agents = new();
        private int _updateCallCount;

        // ── IDtCrowdProvider ─────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters)
        {
            if (_agents.ContainsKey(entity.Index)) return false;
            _agents[entity.Index] = new AgentEntry
            {
                Entity   = entity,
                Params   = parameters,
                Velocity = Vector3.Zero,
                HasTarget = false,
            };
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>Start position is ignored by the fake; falls back to the no-position overload.</remarks>
        public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters, Vector3 startPositionFdp)
            => RegisterAgent(entity, in parameters);

        /// <inheritdoc/>
        public void UnregisterAgent(Entity entity)
        {
            _agents.Remove(entity.Index);
        }

        /// <inheritdoc/>
        public void SetAgentTarget(Entity entity, Vector3 target)
        {
            if (_agents.TryGetValue(entity.Index, out var a))
            {
                a.Target    = target;
                a.HasTarget = true;
                a.ReachedTarget = false;
            }
        }

        /// <inheritdoc/>
        public void Update(float dt, ISimulationView view)
        {
            _updateCallCount++;
            if (_agents.Count == 0 || dt <= 0f) return;

            // Collect positions from ISimulationView.
            var keys     = new List<int>(_agents.Keys);
            keys.Sort();  // deterministic order

            var positions = new Vector3[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                var a = _agents[keys[i]];
                if (view.IsAlive(a.Entity) && view.HasComponent<SimTransform>(a.Entity))
                    positions[i] = view.GetComponentRO<SimTransform>(a.Entity).Position;
                else
                    positions[i] = a.Velocity; // fallback: use last known velocity direction
            }

            // Compute desired velocities.
            var desired = new Vector3[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                var a = _agents[keys[i]];
                if (!a.HasTarget)
                {
                    desired[i] = Vector3.Zero;
                    continue;
                }
                var toTarget = a.Target - positions[i];
                float dist = toTarget.Length();
                if (dist < 0.1f)
                {
                    a.ReachedTarget = true;
                    desired[i] = Vector3.Zero;
                }
                else
                {
                    desired[i] = SafeNormalize(toTarget) * Math.Min(a.Params.MaxSpeed, dist / dt);
                }
            }

            // Separation pass (O(N^2), DD-Fake-Nav §4.3).
            var separation = new Vector3[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                _agents[keys[i]].NearbyAgentCount = 0;
                for (int j = 0; j < keys.Count; j++)
                {
                    if (i == j) continue;
                    float ri = _agents[keys[i]].Params.Radius;
                    float rj = _agents[keys[j]].Params.Radius;
                    float combinedR = ri + rj;
                    var   diff = positions[i] - positions[j];
                    float dist = diff.Length();

                    // NearbyAgentCount: count agents within the wider proximity radius.
                    if (dist < combinedR * NearbyAgentRadiusMultiplier)
                        _agents[keys[i]].NearbyAgentCount++;

                    // Separation force: apply when within the separation radius.
                    if (dist < combinedR * SeparationRadiusMultiplier && dist > 0.0001f)
                    {
                        float separationWeight = _agents[keys[i]].Params.SeparationWeight;
                        // Push formula per §4.3: normalized direction / max(dist, minDist) * weight.
                        separation[i] += SafeNormalize(diff)
                            / MathF.Max(dist, SeparationMinDist)
                            * separationWeight;
                    }
                }
            }

            // Apply acceleration clamping and write final velocities.
            for (int i = 0; i < keys.Count; i++)
            {
                var a       = _agents[keys[i]];
                if (a.HasVelocityOverride)
                {
                    a.Velocity = a.OverriddenVelocity;
                }
                else
                {
                    var target  = desired[i] + separation[i];
                    var delta   = target - a.Velocity;
                    float maxDv = a.Params.MaxAcceleration * dt;
                    if (delta.LengthSquared() > maxDv * maxDv)
                        delta = SafeNormalize(delta) * maxDv;
                    a.Velocity = a.Velocity + delta;
                }
            }
        }

        /// <inheritdoc/>
        public Vector3 GetAgentVelocity(Entity entity)
            => _agents.TryGetValue(entity.Index, out var a) ? a.Velocity : Vector3.Zero;

        /// <inheritdoc/>
        public bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot)
        {
            if (!_agents.TryGetValue(entity.Index, out var a))
            {
                snapshot = default;
                return false;
            }
            snapshot = new CrowdAgentSnapshot
            {
                Position         = Vector3.Zero, // caller must read from ECS
                Velocity         = a.Velocity,
                Target           = a.Target,
                DesiredVelocity  = a.HasTarget ? SafeNormalize(a.Target) * a.Params.MaxSpeed : Vector3.Zero,
                ReachedTarget    = a.ReachedTarget,
                NearbyAgentCount = a.NearbyAgentCount,
            };
            return true;
        }

        // ── IFakeDtCrowdProviderTestApi ──────────────────────────────────────────

        /// <inheritdoc/>
        public IReadOnlyList<int> RegisteredEntityIndices
        {
            get
            {
                var list = new List<int>(_agents.Keys);
                list.Sort();
                return list;
            }
        }

        /// <inheritdoc/>
        public int UpdateCallCount => _updateCallCount;

        /// <inheritdoc/>
        public void OverrideAgentVelocity(Entity entity, Vector3 velocity)
        {
            if (_agents.TryGetValue(entity.Index, out var a))
            {
                a.HasVelocityOverride = true;
                a.OverriddenVelocity  = velocity;
            }
        }

        /// <inheritdoc/>
        public void ClearAgentVelocityOverride(Entity entity)
        {
            if (_agents.TryGetValue(entity.Index, out var a))
                a.HasVelocityOverride = false;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static Vector3 SafeNormalize(Vector3 v)
        {
            float len = v.Length();
            return len > 0.0001f ? v / len : Vector3.Zero;
        }
    }
}

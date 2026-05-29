using System;
using Fdp.Core;
using Fdp.Modules.Geographic.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Modules.Geographic.Systems
{
    /// <summary>
    /// Runs during <see cref="SystemPhase.PostSimulation"/>.
    ///
    /// <para>
    /// Consumes the results written by <see cref="TerrainQuerySolverSystem"/> and writes the
    /// accepted terrain height (<c>HitZ</c>) into the entity's authoritative
    /// <c>SimTransform.Position.Z</c> (3D Cognitive Spatial Awareness promotion, P3D-102).
    /// Terrain altitude is now part of authoritative simulation state — it is no longer rerouted
    /// to a render-only visual offset. The jump-rejection baseline is tracked in
    /// <see cref="TerrainClampBaseline"/>.
    /// </para>
    ///
    /// <para><b>Jump-rejection filter:</b> if the incoming <c>HitZ</c> differs from the
    /// entity's <see cref="TerrainClampBaseline.LastValidIgAltitude"/> by more than
    /// <see cref="JumpRejectionThresholdMeters"/>, the result is discarded and
    /// <c>SimTransform.Position.Z</c> is left unchanged.
    /// This prevents altitude pops at geometry seams, tunnels, or bridge transitions.
    /// </para>
    ///
    /// <para><b>First-frame bootstrap:</b> while <see cref="TerrainClampBaseline.IgAltitudeBaselineEstablished"/>
    /// is 0 the jump-rejection threshold is skipped so the first accepted hit seeds baseline state.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class TerrainQueryResolutionSystem : IEcsModuleSystem
    {
        /// <summary>Maximum Z-delta (metres) that is accepted as a valid terrain hit.</summary>
        public const float JumpRejectionThresholdMeters = 5f;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var world = (EntityRepository)view;

            if (!world.HasSingleton<TerrainQueryBatchData>()) return;
            ref readonly var batch = ref world.GetSingleton<TerrainQueryBatchData>();

            if (batch.Count == 0) return;

            var cmd = view.GetCommandBuffer();

            for (int i = 0; i < batch.Count; i++)
            {
                ref readonly var req = ref batch.Requests[i];
                ref readonly var res = ref batch.Results[i];

                if (!res.HasHit) continue;
                if (!view.IsAlive(req.Entity)) continue;
                if (!view.HasComponent<TerrainClampBaseline>(req.Entity)) continue;
                if (!view.HasComponent<SimTransform>(req.Entity)) continue;

                ref readonly var current = ref view.GetComponentRO<TerrainClampBaseline>(req.Entity);

                // Jump-rejection: skip results that move the reference altitude too abruptly.
                // Bootstrap: first accepted hit for this entity is always applied.
                bool isBootstrap = current.IgAltitudeBaselineEstablished == 0;
                bool withinThreshold = MathF.Abs(res.HitZ - current.LastValidIgAltitude) <= JumpRejectionThresholdMeters;

                if (!isBootstrap && !withinThreshold) continue;

                // Authoritative altitude: write HitZ into SimTransform.Position.Z (Z only,
                // preserving X/Y/rotation). This replaces the former visual-offset reroute.
                ref readonly var tf = ref view.GetComponentRO<SimTransform>(req.Entity);
                cmd.SetComponent(req.Entity, new SimTransform
                {
                    Position = new System.Numerics.Vector3(tf.Position.X, tf.Position.Y, res.HitZ),
                    Rotation = tf.Rotation,
                });

                // Advance the jump-rejection baseline to the accepted altitude.
                cmd.SetComponent(req.Entity, new TerrainClampBaseline
                {
                    LastValidIgAltitude = res.HitZ,
                    IgAltitudeBaselineEstablished = 1,
                });
            }
        }
    }
}

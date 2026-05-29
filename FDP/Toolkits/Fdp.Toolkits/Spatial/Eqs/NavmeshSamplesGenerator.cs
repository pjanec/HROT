using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates positional (EntityId=0) EQS candidates by sampling random reachable
    /// navmesh positions within the sensor's search radius.
    /// </summary>
    public sealed class NavmeshSamplesGenerator : IEqsGenerator
    {
        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return 0;
            if (!repo.HasComponent<SimTransform>(observer)) return 0;

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            // Sim (Z-up) → Recast (Y-up): East=X, altitude=Z→Y (middle slot), North=Y→Z (§0.1).
            // Passing the real altitude lets Recast snap to the correct vertical level (P3D-203).
            var center3D = new Vector3(tf.Position.X, tf.Position.Z, tf.Position.Y);

            // Intermediate stackalloc buffer for raw navmesh points (Recast Y-up).
            Span<Vector3> rawPoints3D = stackalloc Vector3[candidates.Length];
            // TODO NAV-P0-T5: use NavAgentProfile.PreferredLayerMask from ctx.Self
            int rawCount = navmesh.SampleNavmeshPoints(center3D, sensor.SearchRadius, rawPoints3D);

            for (int i = 0; i < rawCount; i++)
            {
                candidates[i] = new EqsResult
                {
                    EntityId  = 0L, // Positional candidate.
                    PositionX = rawPoints3D[i].X,
                    PositionY = rawPoints3D[i].Z, // Recast North (Z) → EQS Y
                    PositionZ = rawPoints3D[i].Y, // Recast altitude (Y) → EQS Z (P3D-203)
                    Score     = 0f,
                    Flags     = 0,
                };
            }

            return rawCount;
        }
    }
}

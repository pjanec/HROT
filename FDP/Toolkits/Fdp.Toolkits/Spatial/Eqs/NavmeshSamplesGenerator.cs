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
            var center3D = new Vector3(tf.Position.X, 0f, tf.Position.Y);

            // Intermediate stackalloc buffer for raw positions (3D flat-earth, Y=0).
            Span<Vector3> rawPoints3D = stackalloc Vector3[candidates.Length];
            // TODO NAV-P0-T5: use NavAgentProfile.PreferredLayerMask from ctx.Self
            int rawCount = navmesh.SampleNavmeshPoints(center3D, sensor.SearchRadius, rawPoints3D);

            for (int i = 0; i < rawCount; i++)
            {
                candidates[i] = new EqsResult
                {
                    EntityId  = 0L, // Positional candidate.
                    PositionX = rawPoints3D[i].X,
                    PositionY = rawPoints3D[i].Z, // north axis mapped to Z in 3D
                    Score     = 0f,
                    Flags     = 0,
                };
            }

            return rawCount;
        }
    }
}

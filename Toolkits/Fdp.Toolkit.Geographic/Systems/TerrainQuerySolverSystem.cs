using System;
using Fdp.Kernel;
using Fdp.Modules.Geographic.Components;
using ModuleHost.Core.Abstractions;

namespace Fdp.Modules.Geographic.Systems
{
    /// <summary>
    /// Runs during <see cref="SystemPhase.Simulation"/>.
    ///
    /// <para>
    /// Delegates the filled <see cref="TerrainQueryBatchData"/> to the
    /// <see cref="ITerrainProvider"/> implementation supplied at construction time.
    /// The provider fills <see cref="TerrainQueryBatchData.Results"/> in-place.
    /// </para>
    ///
    /// <para>
    /// This system does <em>not</em> reset <c>Count</c>; that responsibility belongs to
    /// <see cref="TerrainQueryInitializationSystem"/> at the start of the next frame.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class TerrainQuerySolverSystem : IModuleSystem
    {
        private readonly ITerrainProvider _terrainProvider;

        /// <param name="terrainProvider">Engine terrain provider. Must not be <c>null</c>.</param>
        public TerrainQuerySolverSystem(ITerrainProvider terrainProvider)
        {
            _terrainProvider = terrainProvider ?? throw new ArgumentNullException(nameof(terrainProvider));
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var world = (EntityRepository)view;

            if (!world.HasSingleton<TerrainQueryBatchData>()) return;
            ref var batch = ref world.GetSingleton<TerrainQueryBatchData>();

            if (batch.Count == 0) return;

            _terrainProvider.QueryBatch(batch.Requests, batch.Count, batch.Results);
        }
    }
}

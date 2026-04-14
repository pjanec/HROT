using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Systems;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.IG.Modules
{
    /// <summary>
    /// Optional IG module that activates the terrain ground-clamping pipeline.
    ///
    /// <para>
    /// Install via <c>IgApplication.InstallGroundClamping(ITerrainProvider)</c>.
    /// When not installed the <see cref="TerrainQueryBatchData"/> singleton is never
    /// created and no terrain queries are issued; all other IG systems are unaffected.
    /// </para>
    ///
    /// <para><b>Registered systems (in phase order):</b>
    /// <list type="number">
    ///   <item><see cref="TerrainQueryInitializationSystem"/> — Input: resets batch counter.</item>
    ///   <item><see cref="TerrainQuerySubmitSystem"/> — Input: fills batch from clamped entities.</item>
    ///   <item><see cref="TerrainQuerySolverSystem"/> — Simulation: calls <see cref="ITerrainProvider.QueryBatch"/>.</item>
    ///   <item><see cref="TerrainQueryResolutionSystem"/> — PostSimulation: updates <see cref="GroundClampingState"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class IgGroundClampingModule : IEcsModule
    {
        private readonly ITerrainProvider _terrainProvider;

        public string          Name   => "IgGroundClamping";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <param name="terrainProvider">
        /// Engine adapter that performs the actual terrain-height sampling.
        /// </param>
        public IgGroundClampingModule(ITerrainProvider terrainProvider)
        {
            _terrainProvider = terrainProvider;
        }

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new TerrainQueryInitializationSystem());
            registry.RegisterSystem(new TerrainQuerySubmitSystem());
            registry.RegisterSystem(new TerrainQuerySolverSystem(_terrainProvider));
            registry.RegisterSystem(new TerrainQueryResolutionSystem());
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { }
    }
}

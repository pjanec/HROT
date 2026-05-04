using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// SoD (Separation-of-Duties) module that drives the
    /// <see cref="Systems.AreaQuerySolverSystem"/> at 10 Hz on a background thread.
    ///
    /// <para>Registered on the Muscle node so it can access the
    /// <c>SpatialGridData</c> singleton and the entity spatial hash grid for
    /// polygon-area entity queries submitted by Brain BTree nodes.</para>
    ///
    /// <para>Also registers <see cref="Systems.AreaQueryResultMaterializationSystem"/>
    /// so that result events published by the solver are materialized into the
    /// <see cref="Fdp.Toolkit.Spatial.Eqs.AreaQueryBatchData"/> ring buffer on the
    /// main thread before the Brain Simulation phase each frame.</para>
    /// </summary>
    public sealed class EqsModule : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "Eqs";

        /// <inheritdoc/>
        /// <remarks>Runs asynchronously at 10 Hz against a SoD snapshot.</remarks>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        private readonly Systems.AreaQuerySolverSystem _solver = new();

        /// <inheritdoc/>
        /// <remarks>
        /// Registers <see cref="Systems.AreaQueryResultMaterializationSystem"/> so the
        /// module host runs it each frame on the main thread, materializing results before
        /// the BTree Simulation phase.
        /// </remarks>
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new Systems.AreaQueryResultMaterializationSystem());
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime)
            => _solver.Execute(view, deltaTime);
    }
}

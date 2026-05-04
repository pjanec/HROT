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
        /// Uses the Direct Execution pattern — all logic is delegated to
        /// <see cref="Systems.AreaQuerySolverSystem.Execute"/>.
        /// </remarks>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime)
            => _solver.Execute(view, deltaTime);
    }
}

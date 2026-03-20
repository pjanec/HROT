using ModuleHost.Core.Abstractions;
using FDP.Toolkit.NetworkSpawning.Systems;

namespace Fdp.Examples.NetworkDemo.Modules
{
    /// <summary>
    /// Thin module wrapper hosting <see cref="NetworkSpawningSystem"/>.
    /// Runs synchronously in the main simulation loop, consuming
    /// <c>SpawnEntityCommand</c>, <c>UpdateEntityCommand</c>, and
    /// <c>DestroyEntityCommand</c> events from the world bus each frame.
    /// </summary>
    public class SpawningModule : IEcsModule
    {
        public string Name => "NetworkSpawning";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkSpawningSystem _system;

        public SpawningModule(NetworkSpawningSystem system)
            => _system = system;

        public void RegisterSystems(ISystemRegistry registry)
            => registry.RegisterSystem(_system);

        public void Tick(ISimulationView view, float dt) { }
    }
}

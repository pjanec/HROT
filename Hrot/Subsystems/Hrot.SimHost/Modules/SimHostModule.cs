using Fdp.Interfaces;
using FDP.Toolkit.NetworkSpawning.Systems;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Modules
{
    // ─── Module ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hosts a <see cref="FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>.
    /// Entity lifecycle (create/delete request handling) is a brain (CGF) responsibility
    /// and must NOT be wired here. Network translators are provided by the composition
    /// root via <c>INetworkFactory</c> and registered separately.
    /// </summary>
    public class SimHostModule : IEcsModule
    {
        public string         Name   => "SimHost";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkSpawningSystem _spawnSystem;

        public SimHostModule(NetworkSpawningSystem spawnSystem)
        {
            _spawnSystem = spawnSystem;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(_spawnSystem);
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}

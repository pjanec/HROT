using System;
using Bagira.BDC.SSTD;
using Fdp.Interfaces;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Modules
{
    /// <summary>
    /// Thin module wrapper that hosts <see cref="NetworkSpawningSystem"/> in the IG kernel.
    ///
    /// Consumes <c>SpawnEntityCommand</c>, <c>UpdateEntityCommand</c>, and
    /// <c>DestroyEntityCommand</c> managed events each frame and drives the ELM/ECS
    /// entity lifecycle machinery.
    ///
    /// Must be registered in the kernel <em>before</em> <c>CycloneNetworkModule</c> so
    /// that entities are available for the first ingress tick.
    /// </summary>
    public class SpawningModule : IEcsModule
    {
        public string          Name   => "NetworkSpawning";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkSpawningSystem _system;

        public SpawningModule(NetworkSpawningSystem system)
            => _system = system ?? throw new ArgumentNullException(nameof(system));

        public void RegisterSystems(ISystemRegistry registry)
            => registry.RegisterSystem(_system);

        public void Tick(ISimulationView view, float dt) { }
    }
}

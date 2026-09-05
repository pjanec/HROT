using System;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// An <see cref="IEcsModule"/> that exists only to carry ONE system into a phase.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a module is needed at all.</b> <c>RegisterGlobalSystem</c> rejects
    /// <see cref="SystemPhase.Simulation"/>, so a lone simulation-phase system has to reach the kernel
    /// through <c>RegisterModule</c>. That is a scheduling constraint, not a modelling one — the system has
    /// no module-shaped behaviour to express, and the wrapper's <c>Tick</c> is empty.</para>
    ///
    /// <para><b>Why it is shared (<c>B2</c>).</b> Two hosts had each written their own identical wrapper —
    /// <c>SimHostModule</c> (six call sites) and <c>IgUnitHierarchyModule</c> (one) — differing only in the
    /// <see cref="Name"/> string. Neither carried host-specific logic. The composition work replaces
    /// host-named plumbing with role-selected capabilities, and a per-host class for "one system in a
    /// phase" is exactly the host-shaped seam that has to go first, or every new role grows another one.</para>
    ///
    /// <para><b>Name it after the SYSTEM, not the host.</b> The name reaches diagnostics and the module
    /// listing, and a module called "SimHost" that contains a single spawning system tells a reader the
    /// wrong thing about which node it belongs to.</para>
    /// </remarks>
    public sealed class SingleSystemModule : IEcsModule
    {
        private readonly IEcsModuleSystem _system;

        /// <param name="name">Diagnostic name — describe the SYSTEM's job, not the host.</param>
        /// <param name="system">The one system this module registers.</param>
        public SingleSystemModule(string name, IEcsModuleSystem system)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A module name is required.", nameof(name));

            Name    = name;
            _system = system ?? throw new ArgumentNullException(nameof(system));
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(_system);

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { }
    }
}

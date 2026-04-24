using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Infrastructure
{
    public abstract class LegacySystemGroupAdapterBase : IEcsModuleSystem, ISystemGroup
    {
        private sealed class LegacyComponentSystemAdapter : IEcsModuleSystem, IProfiledSystem
        {
            private readonly ComponentSystem _inner;
            public string ProfileName { get; }

            public LegacyComponentSystemAdapter(ComponentSystem inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                ProfileName = inner.GetType().Name;
            }

            public void Execute(ISimulationView view, float deltaTime) => _inner.Run();
        }

        private readonly IReadOnlyList<IEcsModuleSystem> _systems;

        protected LegacySystemGroupAdapterBase(SystemGroup group, string name)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _systems = group.GetSystems()
                .Select(static s => (IEcsModuleSystem)new LegacyComponentSystemAdapter(s))
                .ToList();
        }

        public string Name { get; }

        public void Execute(ISimulationView view, float deltaTime) { }

        public IReadOnlyList<IEcsModuleSystem> GetSystems() => _systems;
    }

    /// <summary>
    /// Adapter that bridges a legacy <see cref="SystemGroup"/> into the
    /// <c>ModuleHostKernel</c> simulation dispatch pipeline.
    ///
    /// <para>
    /// Because <c>SystemPhase.Simulation</c> is reserved for module dispatch and is
    /// never executed by the kernel's global-system scheduler, this type is registered
    /// as an <see cref="IEcsModule"/> (not a global system).  Each inner system is
    /// surfaced to the Architecture Diagnostics Window via
    /// <see cref="ISystemRegistry.RegisterManualSystem{T}"/>, placing it in the
    /// <c>Simulation</c> bucket for correct phase labelling.  The module then ticks
    /// the profiled wrappers manually during its own <see cref="Tick"/> call so that
    /// execution timing is recorded without double-execution.
    /// </para>
    /// </summary>
    public sealed class SimulationGroupModule : IEcsModule
    {
        // Adapter placed in SystemPhase.Simulation for diagnostics grouping.
        // The kernel never auto-executes this phase for global systems; the module
        // ticks these wrappers manually from Tick() below.
        [UpdateInPhase(SystemPhase.Simulation)]
        private sealed class SimLegacySystemAdapter : IEcsModuleSystem, IProfiledSystem
        {
            private readonly ComponentSystem _inner;
            public string ProfileName => _inner.GetType().Name;
            public SimLegacySystemAdapter(ComponentSystem inner) => _inner = inner;
            public void Execute(ISimulationView view, float deltaTime) => _inner.Run();
        }

        private readonly SystemGroup _legacyGroup;
        private readonly List<IEcsModuleSystem> _profiledSystems = new();

        /// <inheritdoc/>
        public string Name => "SimulationGroup";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public SimulationGroupModule(SystemGroup legacyGroup)
        {
            _legacyGroup = legacyGroup ?? throw new ArgumentNullException(nameof(legacyGroup));
        }

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            // Register each legacy system in the Simulation diagnostics bucket so the
            // Architecture Diagnostics Window shows them under the correct phase header.
            // RegisterManualSystem calls RegisterSystem (which reads [UpdateInPhase]) but
            // returns a ProfiledManualSystemWrapper that the module ticks manually below.
            foreach (var sys in _legacyGroup.GetSystems())
            {
                var adapter = new SimLegacySystemAdapter(sys);
                var profiledWrapper = registry.RegisterManualSystem(adapter);
                _profiledSystems.Add(profiledWrapper);
            }
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime)
        {
            foreach (var sys in _profiledSystems)
            {
                sys.Execute(view, deltaTime);
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Type>? GetRequiredComponents() => null;
    }

    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class PostSimulationGroupAdapter : LegacySystemGroupAdapterBase
    {
        public PostSimulationGroupAdapter(SystemGroup group) : base(group, "PostSimulationGroup") { }
    }
}


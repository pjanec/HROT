using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Infrastructure
{
    /// <summary>
    /// Wraps a <see cref="SystemGroup"/> so that its systems execute during the
    /// kernel's <see cref="SystemPhase.Input"/> phase.
    ///
    /// <para>Used by <c>CgfSubsystem</c> and <c>EditorSubsystem</c> to run
    /// input-phase Brain systems (e.g. <c>MissionControlExecutionSystem</c>,
    /// <c>DoctrineIngressSystem</c>) before the simulation-phase group ticks.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class CgfInputGroupAdapter : IEcsModuleSystem, ISystemGroup
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

        public string Name => "InputGroup";

        /// <param name="group">The system group to run during the Input phase.</param>
        public CgfInputGroupAdapter(SystemGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            _systems = group.GetSystems()
                .Select(static s => (IEcsModuleSystem)new LegacyComponentSystemAdapter(s))
                .ToList();
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime) { }

        /// <inheritdoc/>
        public IReadOnlyList<IEcsModuleSystem> GetSystems() => _systems;
    }
}

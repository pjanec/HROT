using System;
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
    public sealed class CgfInputGroupAdapter : IEcsModuleSystem
    {
        private readonly SystemGroup _group;

        /// <param name="group">The system group to run during the Input phase.</param>
        public CgfInputGroupAdapter(SystemGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            _group = group;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            _group.Run();
        }
    }
}

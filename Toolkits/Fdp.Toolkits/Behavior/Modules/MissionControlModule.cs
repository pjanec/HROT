using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Grouping for top-down command processing systems: doctrine assignment and
    /// multi-phase mission plan advancement.
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="DoctrineIngressSystem"/> — applies AssignDoctrineEvents to entities</item>
    ///   <item><see cref="MissionDirectorSystem"/> — advances MissionPlanQueue phases</item>
    /// </list>
    ///
    /// <para>Belongs to the <em>Brain</em> tier of the Brain/Muscle decomposition (MOD1 §3.2.2).</para>
    /// </summary>
    public sealed class MissionControlModule
    {
        private readonly DoctrineRegistry _registry;

        /// <summary>Systems that run in the Input phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        public MissionControlModule(DoctrineRegistry registry)
        {
            _registry = registry;
            InputSystems = new IEcsModuleSystem[]
            {
                new DoctrineIngressSystem(_registry),
            };
            SimulationSystems = new IEcsModuleSystem[]
            {
                new MissionDirectorSystem(),
            };
        }
    }
}

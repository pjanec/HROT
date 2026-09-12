using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Grouping for top-down command processing systems: behavior assignment and
    /// multi-phase mission plan advancement.
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="BehaviorIngressSystem"/> — applies AssignBehaviorEvents to entities</item>
    ///   <item><see cref="MissionDirectorSystem"/> — advances MissionPlanQueue phases</item>
    /// </list>
    ///
    /// <para>Belongs to the <em>Brain</em> tier of the Brain/Muscle decomposition (MOD1 §3.2.2).</para>
    /// </summary>
    public sealed class MissionControlModule
    {
        private readonly BehaviorRegistry _registry;

        /// <summary>Systems that run in the Input phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        /// <param name="gateOnAuthority">⭐ <c>P3</c> step <c>3b</c> — process only missions whose
        /// entity's cognitive state this node owns. ⚠ Defaults to <c>false</c>; see
        /// <c>CognitiveRuntimeModule</c>'s parameter of the same name for why the default is
        /// load-bearing.</param>
        public MissionControlModule(BehaviorRegistry registry, bool gateOnAuthority = false)
        {
            _registry = registry;
            InputSystems = new IEcsModuleSystem[]
            {
                new BehaviorIngressSystem(_registry),
            };
            SimulationSystems = new IEcsModuleSystem[]
            {
                new MissionDirectorSystem(gateOnAuthority),
            };
        }
    }
}

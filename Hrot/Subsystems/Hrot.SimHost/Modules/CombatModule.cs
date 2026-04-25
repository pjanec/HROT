using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Systems;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// Grouping for combat, perception, and physics systems that are present on all node roles.
    ///
    /// <para><b>Systems registered (in execution order):</b></para>
    /// <list type="number">
    ///   <item><b>Input phase</b> — <see cref="FireProcessingSystem"/>, <see cref="RaycastSolverSystem"/>, <see cref="HitResolutionSystem"/></item>
    ///   <item><b>Post-sim phase</b> — <see cref="BallisticsSystem"/></item>
    /// </list>
    ///
    /// <para>
    /// <see cref="Fdp.Toolkit.Behavior.Systems.HsmDamageBridgeSystem"/> was relocated
    /// to <see cref="Fdp.Toolkit.Behavior.Modules.CognitiveRuntimeModule"/> (PACK-M001)
    /// so it runs on the Brain tier where <see cref="Fdp.Toolkit.Behavior.Components.BrainHsm128"/>
    /// components reside.
    /// </para>
    /// </summary>
    public sealed class CombatModule
    {
        /// <summary>Systems that run in the Input phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems that run in the PostSimulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }

        public CombatModule()
        {
            InputSystems = new IEcsModuleSystem[]
            {
                new FireProcessingSystem(),
                new RaycastSolverSystem(),
                new HitResolutionSystem(),
            };
            PostSimulationSystems = new IEcsModuleSystem[]
            {
                new BallisticsSystem(),
            };
        }
    }
}

using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Grouping for per-frame AI evaluation systems: behavior tree and HSM stepping,
    /// and channel arbitration.
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="ChannelArbitrationSystem"/> — clears stale channels on doctrine change</item>
    ///   <item><see cref="HsmDamageBridgeSystem"/> — bridges capability-loss events into HSM (PACK-M001)</item>
    ///   <item><see cref="BTreeTickSystem"/> — zero-alloc BTree tick per entity</item>
    ///   <item><see cref="HsmTickSystem{BrainHsm128}"/> — HSM tick for 128-byte HSM instances</item>
    ///   <item><see cref="HsmTickSystem{BrainHsm64}"/> — HSM tick for 64-byte HSM instances</item>
    /// </list>
    ///
    /// <para>Belongs to the <em>Brain</em> tier of the Brain/Muscle decomposition (MOD1 §3.2.3).</para>
    /// </summary>
    public sealed class CognitiveRuntimeModule
    {
        private readonly DoctrineRegistry _registry;

        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        public CognitiveRuntimeModule(DoctrineRegistry registry)
        {
            _registry = registry;
            SimulationSystems = new IEcsModuleSystem[]
            {
                new ChannelArbitrationSystem(),
                new HsmDamageBridgeSystem(),           // PACK-M001: before HSM ticks
                new BTreeTickSystem(_registry),
                new HsmTickSystem<BrainHsm128>(_registry),
                new HsmTickSystem<BrainHsm64>(_registry),
            };
        }
    }
}

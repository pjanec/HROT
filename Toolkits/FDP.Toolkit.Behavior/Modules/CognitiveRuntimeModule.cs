using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;

namespace FDP.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Grouping for per-frame AI evaluation systems: behavior tree and HSM stepping,
    /// and channel arbitration.
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="ChannelArbitrationSystem"/> — clears stale channels on doctrine change</item>
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

        public CognitiveRuntimeModule(DoctrineRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Registers the channel arbitration, BTree tick, and HSM tick systems into the provided group.
        /// </summary>
        public void RegisterSystems(SystemGroup group)
        {
            group.AddSystem(new ChannelArbitrationSystem());
            group.AddSystem(new BTreeTickSystem(_registry));
            group.AddSystem(new HsmTickSystem<BrainHsm128>(_registry));
            group.AddSystem(new HsmTickSystem<BrainHsm64>(_registry));
        }
    }
}

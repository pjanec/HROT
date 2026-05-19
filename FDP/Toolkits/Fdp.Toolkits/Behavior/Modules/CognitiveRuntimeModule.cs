using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Grouping for per-frame AI evaluation systems: behavior tree and HSM stepping,
    /// plus channel arbitration.
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="bullet">
    ///   <item><b>Input phase</b>: none (diagnostics moved to BehaviorDiagnosticsModule).</item>
    ///   <item><b>Simulation phase</b>:
    ///     <list type="number">
    ///       <item><see cref="ChannelArbitrationSystem"/> — clears stale channels on behavior change</item>
    ///       <item><see cref="CognitiveInterruptSystem"/> — edge-triggered blackboard interrupt bytes</item>
    ///       <item><see cref="BTreeTickSystem"/> — zero-alloc BTree tick per entity</item>
    ///       <item><see cref="HsmTickSystem{BrainHsm128}"/> — HSM tick for 128-byte HSM instances</item>
    ///       <item><see cref="HsmTickSystem{BrainHsm64}"/> — HSM tick for 64-byte HSM instances</item>
    ///       <item><see cref="CognitiveCleanupSystem"/> — clears per-frame interrupt bytes after all brain ticks</item>
    ///     </list>
    ///   </item>
    /// </list>
    ///
    /// <para>Belongs to the <em>Brain</em> tier of the Brain/Muscle decomposition (MOD1 §3.2.3).</para>
    /// </summary>
    public sealed class CognitiveRuntimeModule
    {
        private readonly BehaviorRegistry _registry;

        /// <summary>Systems that run in the Input phase (Behavior diagnostics ingress).</summary>
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        public CognitiveRuntimeModule(BehaviorRegistry registry)
        {
            _registry = registry;
            InputSystems = System.Array.Empty<IEcsModuleSystem>();
            SimulationSystems = new IEcsModuleSystem[]
            {
                new ChannelArbitrationSystem(),
                new CognitiveInterruptSystem(),            // BHU-008: before HSM/BTree ticks
                new BTreeTickSystem(_registry),
                new HsmTickSystem<BrainHsm128>(_registry),
                new HsmTickSystem<BrainHsm64>(_registry),
                new CognitiveCleanupSystem(),              // BHU-015: clears interrupt bytes last
            };
        }
    }
}

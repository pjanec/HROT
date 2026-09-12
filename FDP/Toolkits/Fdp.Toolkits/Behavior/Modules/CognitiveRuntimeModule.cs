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
    ///       <item><see cref="BehaviorFrameSystem"/> — advances the global behaviour-frame pulse (Q46 rule 2b)</item>
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

        /// <param name="gateOnAuthority">
        /// ⭐⭐⭐ <c>P3</c> step <c>3b</c> — when true, EVERY system below processes only entities whose
        /// cognitive state this node owns. 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.5.
        ///
        /// <para>⭐⭐ <b>All six move together, deliberately.</b> They are one pipeline over one set of
        /// entities: <c>ChannelArbitrationSystem</c>, <c>CognitiveInterruptSystem</c> and
        /// <c>CognitiveCleanupSystem</c> all WRITE cognitive state, so gating only the two tick systems
        /// would leave a node still clobbering a brain another node owns — half a fix that looks whole.</para>
        ///
        /// <para>⚠ <b>Defaults to <c>false</c>, and the default is load-bearing.</b> A promoted ghost owns
        /// nothing until a role policy or an explicit grant says otherwise, so enabling this before the
        /// host has a policy would stop it processing every entity it did not create — reproducing
        /// <c>CE-256</c> while fixing it. ⇒ turn it on in the same breath as handing over the policy
        /// (step 4).</para>
        /// </param>
        public CognitiveRuntimeModule(BehaviorRegistry registry, bool gateOnAuthority = false)
        {
            _registry = registry;
            InputSystems = System.Array.Empty<IEcsModuleSystem>();
            SimulationSystems = new IEcsModuleSystem[]
            {
                new ChannelArbitrationSystem(gateOnAuthority),
                new CognitiveInterruptSystem(gateOnAuthority),   // BHU-008: before HSM/BTree ticks
                new BTreeTickSystem(_registry, gateOnAuthority),
                new HsmTickSystem<BrainHsm128>(_registry, gateOnAuthority),
                new HsmTickSystem<BrainHsm64>(_registry, gateOnAuthority),
                new CognitiveCleanupSystem(gateOnAuthority),     // BHU-015: clears interrupt bytes last
                // ⭐⭐⭐ Batch 94 (94b) — the behaviour-frame pulse, LAST, so it means "a brain tick
                //    HAS RUN". Q46 §2 rule 2b: ONE tick source for every host, gated on dt > 0.
                // ⚠ The position is intentional but NOT load-bearing — BehaviorFrame is an edge
                //    detector read at draw time, and BlueprintTickSystem is in another module and
                //    could not be ordered against this one anyway.
                new BehaviorFrameSystem(),
            };
        }
    }
}

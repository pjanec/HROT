using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Clears per-frame interrupt fields in <see cref="BrainBlackboard"/> at the end of the
    /// simulation tick.  <see cref="BrainBlackboard.Interrupt_MobilityLost"/> and
    /// <see cref="BrainBlackboard.Interrupt_Reserved"/> are one-shot signals written by
    /// <see cref="CognitiveInterruptSystem"/>; they must be cleared each frame so that
    /// edge-triggered logic in the brain systems does not fire on subsequent ticks.
    ///
    /// <para>
    /// Must run as the LAST system in <see cref="Modules.CognitiveRuntimeModule"/> so that
    /// HSM and BTree tick systems can read the bytes during the same frame.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    internal sealed class CognitiveCleanupSystem : IEcsModuleSystem
    {
        /// <summary>
        /// ⭐⭐⭐ <c>P3</c> step <c>3b</c> — the EXECUTION gate; see
        /// <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.5. ⚠ Defaults to <c>false</c>: a promoted
        /// ghost owns nothing until a policy or grant says otherwise, so enabling it before the node has
        /// a policy would stop it processing every entity it did not create.
        /// </summary>
        private readonly bool _gateOnAuthority;

        public CognitiveCleanupSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            // ⭐⭐ P3 step 3b — this WRITES (GetComponentRW) into every blackboard it finds, so an
            //   un-gated node clobbers interrupt bits on a brain another node owns. Gated on
            //   BrainBlackboard, the only component it required.
            var q = repo.Query().WithOwnedWhen<BrainBlackboard>(_gateOnAuthority).Build();
            foreach (var entity in q)
            {
                ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
                bb.Interrupt_MobilityLost = 0;
                bb.Interrupt_Reserved     = 0;
            }
        }
    }
}

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
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            var q = repo.Query().With<BrainBlackboard>().Build();
            foreach (var entity in q)
            {
                ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
                bb.Interrupt_MobilityLost = 0;
                bb.Interrupt_Reserved     = 0;
            }
        }
    }
}

using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐⭐ Advances <see cref="BehaviorFrame"/> once per <b>non-frozen</b> simulation step.
    ///
    /// <para>📄 <c>Architect_Question_46…md</c> §2 rule 2b — the user's specification: one tick source
    /// for every host, because <i>"the brain (cgf) does not tick ANY behavior when dt=0"</i>.</para>
    ///
    /// <para>⛔⛔ <b>The <c>dt</c> gate is the entire contract.</b> Without it this would be the world
    /// tick, which advances while the debugger holds time — and a watch panel sampling on that would
    /// clear its change highlight under a breakpoint. 📌 Batch 68 refused exactly that.</para>
    ///
    /// <para>⭐ <b>Ordered last in <see cref="Modules.CognitiveRuntimeModule"/></b>, after
    /// <c>CognitiveCleanupSystem</c>, so the pulse means <i>"a brain tick HAS RUN"</i> rather than
    /// <i>"one is about to"</i>. ⚠ <b>But the placement is not load-bearing</b> — see
    /// <see cref="BehaviorFrame"/>: readers sample at draw time, so the bump is only an edge.
    /// <c>BlueprintTickSystem</c> lives in another module and cannot be ordered against this one;
    /// ⭐ that is fine for the same reason.</para>
    ///
    /// <para>⛔ <b>It touches no entity and holds no state</b>, so it is exempt from the
    /// <c>EntityRepository</c> cast every sibling system performs.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    internal sealed class BehaviorFrameSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            BehaviorFrame.Advance();
        }
    }
}

using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Lifecycle.Systems
{
    /// <summary>
    /// Processes lifecycle events (ACKs) and manages entity state transitions.
    /// Runs in BeforeSync phase to ensure changes are visible to all modules.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class LifecycleSystem : IEcsModuleSystem
    {
        private readonly EntityLifecycleModule _manager;

        /// <summary>
        /// ⭐⭐⭐ <b>The replay gate.</b> Asked ONCE per <see cref="Execute"/>; when it returns <c>true</c>
        /// this system does nothing at all.
        ///
        /// <para>📄 <c>docs/designs/mgmt-1/DESIGN.md</c> §8.10 rules that during replay <i>"the ELM pipeline
        /// is never invoked"</i> — entities materialise straight from the recorded
        /// <c>EntityHeader.LifecycleState</c> — and §8.5 gives the reason: <i>"if ELM were re-enabled between
        /// seeks, entities in-flight over DDS would stall in <c>Constructing</c> waiting for ACKs from a node
        /// that is only replaying recorded data."</i></para>
        ///
        /// <para>⛔⛔ <b>DELIBERATE DEVIATION from §8.10's prescribed MECHANISM</b> *(authorised
        /// <c>2026-09-12</c>; 📄 <c>docs/designs/replay-and-modules/DESIGN.md</c> §2.1m)*. §8.10 says to put
        /// this system inside <c>NetworkLifecycleSystemGroup</c>. 📐 Measured: that group's
        /// <c>ExecuteGroup</c> has <b>exactly ONE caller</b> — <c>NedReplicationModule.Tick</c> — so it never
        /// ticks on the editor (<c>NullReplicationModule</c>) or on BDC nodes ⇒ relocating would STOP entity
        /// lifecycle on those hosts, and a system in both the group and the scheduler would run TWICE a
        /// frame. ⭐ Gating in place delivers §8.10's intent on EVERY host instead.</para>
        ///
        /// <para>⭐ Shape borrowed from the seam that already does this well —
        /// <c>CycloneNetworkIngressSystem.IsWorldStateFrozen</c>. ⚠ Unset (the default) means "never
        /// replaying", so a host that does not wire it behaves exactly as before.</para>
        /// </summary>
        public System.Func<bool>? IsReplayActive { get; set; }

        public LifecycleSystem(EntityLifecycleModule manager)
        {
            _manager = manager;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // ⛔ The log owns the world during playback: no acks, no drains, and — decisively — no
            // CheckTimeouts, whose unsigned frame subtraction wraps once a seek rewinds the counter.
            if (IsReplayActive != null && IsReplayActive()) return;

            var cmd = view.GetCommandBuffer();
            uint currentFrame = view.Tick;

            // ⭐ Before anything else: if a world replacement armed a resume, re-open the protocols the
            // restored world implies. A no-op unless armed. 📄 DESIGN.md §2.1m step 2.
            _manager.ResumeFromRestoredWorld(view, currentFrame, cmd);

            // Process construction ACKs
            var constructionAcks = view.ReadEvents<ConstructionAck>();
            foreach (var ack in constructionAcks)
            {
                _manager.ProcessConstructionAck(ack, currentFrame, cmd);
            }
            
            // Process destruction ACKs
            var destructionAcks = view.ReadEvents<DestructionAck>();
            foreach (var ack in destructionAcks)
            {
                _manager.ProcessDestructionAck(ack, currentFrame, cmd);
            }
            
            // Drain zero-participant pending constructions/destructions after minimum one-frame delay
            _manager.DrainInstantComplete(cmd, currentFrame);

            // Check for timeouts
            _manager.CheckTimeouts(currentFrame, cmd);
        }
    }
}

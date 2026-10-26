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
    [TickMode(TickPolicy.EveryFrame)]
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
        /// <c>CycloneNetworkIngressSystem.IsWorldStateFrozen</c>. ⭐ CE-3329 P2 — the gate is the world's
        /// <see cref="Fdp.Toolkit.Replay.ReplayState"/>; ⛔ SUPERSEDED: an injected <c>Func&lt;bool&gt;? IsReplayActive</c>.</para>
        /// </summary>

        private int _restoresSeen = -1;

        public LifecycleSystem(EntityLifecycleModule manager)
        {
            _manager = manager;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // ⛔ The log owns the world during playback: no acks, no drains, and — decisively — no
            // CheckTimeouts, whose unsigned frame subtraction wraps once a seek rewinds the counter.
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;

            var cmd = view.GetCommandBuffer();
            uint currentFrame = view.Tick;

            // ⭐ R-3039 S3 — the world was put back in place (AfterRestore.Run: a dry-run exit, a checkpoint restore): the in-flight
            //   handshakes named the replaced entities, so they are discarded and re-derived from the restored lifecycle states — the
            //   replay design's §2.1m re-derive, until now wired only into the editor preview. ⛔ A load does not count (its new
            //   entities' constructions are already being driven). 📄 docs/DESIGN_System_State_Inventory.md §8.
            int restores = Fdp.Toolkit.Replication.Services.WorldEpoch.RestoresOf(view);
            if (_restoresSeen >= 0 && restores != _restoresSeen) _manager.OnWorldReplaced(resumingToLive: true);
            _restoresSeen = restores;

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

            // ⭐ AQ107 (CE-3323) — announce what finished this tick, on every node (§3.4e)
            _manager.PublishOutcomes(view, cmd);
        }
    }
}

using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// CE-291 (piece C, C5): the FIRST real peer-side participant of the reliable-init barrier — a
    /// <b>poll</b> <see cref="DeferredConstructionParticipant"/> that holds a reliable remote GHOST in
    /// <see cref="EntityLifecycle.Constructing"/> for a simulated local-initialisation window, then acks so
    /// the ghost promotes to <see cref="EntityLifecycle.Active"/> and
    /// <c>PeerLifecycleStatusEgressSystem</c> reports the peer Active to the creator.
    ///
    /// <para>⭐ <b>Why SIMULATED and not a real navmesh/model query.</b> The design (§1.2, §3a.5) names a
    /// <c>NavmeshReadinessParticipant</c> (SimHost/muscle: wait for the navmesh tile at the spawn) and a
    /// <c>ModelLoadReadinessParticipant</c> (IG: wait for the entity model to stream). 🔒 <b>User steer
    /// (<c>2026-09-16</c>):</b> <i>"there is no real navmesh in simhost and no real model loading in ig so
    /// … fake the waiting for initializing them."</i> ⇒ this ONE generic participant stands in for both:
    /// it fakes the local-init wait with a fixed frame delay. When the real subsystems exist, they replace
    /// the <see cref="TryComplete"/> poll with an actual readiness query; the barrier plumbing around it is
    /// unchanged. Barrier design <c>docs/DESIGN_Cross_Node_Construction_Barrier.md</c> §3b/§3c.</para>
    ///
    /// <para>⭐ <b>Scope — reliable ghosts only.</b> It participates for every entity (global, like the
    /// gateway) but defers ONLY entities tagged <see cref="ReportLifecycleOnActive"/> — the transient tag
    /// <c>EntityMasterIngressTranslator</c> puts on a ghost whose <c>EntityMaster</c> carried the
    /// <c>WaitForAcks</c> bit. A fast-mode ghost and the creator's own local copy (neither is tagged) are
    /// acked the same frame, so nothing outside reliable init changes behaviour.</para>
    ///
    /// <para>⭐ <b>Never self-force-activates (§3b.3).</b> The base <see cref="OnTimeout"/> default
    /// force-acks a stalled poll participant; a reliable ghost must NOT — it waits for its condition or for
    /// the creator's <c>EntityMaster</c> dispose. So <see cref="OnTimeout"/> is overridden to keep waiting
    /// (one loud stall log), leaving the abort to the creator. That is exactly what makes this the
    /// "stuck but participating" peer the creator's timeout=abort leg needs: it sends the phase-1
    /// <c>Constructing</c> status (so C2 does not prune it as unsupported) yet never reaches Active.</para>
    ///
    /// <para>⚠ <b>Off by default.</b> Registered only when the simulated window is &gt; 0 (the host wires it
    /// from <c>FDP_FAKE_INIT_FRAMES</c>). With no fake init there is nothing to wait for, so the barrier
    /// completes as soon as each peer ghosts + reports — production behaviour is unchanged.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public sealed class SimulatedInitReadinessParticipant : DeferredConstructionParticipant
    {
        private readonly int _simulatedReadyFrames;
        private readonly string _label;

        // Per-entity frame the ghost was deferred, so TryComplete can measure the elapsed window.
        private readonly Dictionary<Entity, uint> _deferFrame = new();
        // The stall log fires once per entity, and only after the window is well past.
        private readonly HashSet<Entity> _stallLogged = new();

        /// <summary>How long after the window a still-pending ghost logs a stall (it never self-acks).</summary>
        private const uint STALL_LOG_MARGIN_FRAMES = 300;

        /// <param name="moduleId">ELM module id this participant acks under (distinct from the gateway's).</param>
        /// <param name="elm">The entity lifecycle module driving construction.</param>
        /// <param name="simulatedReadyFrames">Frames a reliable ghost is held before it is acked (the fake init).</param>
        /// <param name="label">A human label for logs — e.g. "navmesh" on SimHost, "model-load" on IG.</param>
        public SimulatedInitReadinessParticipant(
            int moduleId,
            EntityLifecycleModule elm,
            int simulatedReadyFrames,
            string label = "sim-init")
            : base(moduleId, elm)
        {
            _simulatedReadyFrames = simulatedReadyFrames > 0 ? simulatedReadyFrames : 1;
            _label = label;
        }

        /// <summary>Global — inspects every constructed entity (filtering happens in <see cref="TryImmediateComplete"/>).</summary>
        protected override bool Participates(ISimulationView view, Entity entity, long blueprintId) => true;

        /// <summary>Ack immediately unless this is a reliable ghost (tagged <see cref="ReportLifecycleOnActive"/>).</summary>
        protected override bool TryImmediateComplete(ISimulationView view, Entity entity)
            => !view.HasComponent<ReportLifecycleOnActive>(entity);

        /// <summary>Start the simulated-init clock for a deferred reliable ghost.</summary>
        protected override void OnDeferred(ISimulationView view, Entity entity)
        {
            _deferFrame[entity] = GetFrame(view);
            if (FdpLog<SimulatedInitReadinessParticipant>.IsDebugEnabled)
                FdpLog<SimulatedInitReadinessParticipant>.Debug(
                    "[{0}] Entity {1}: simulating local init for {2} frames.",
                    _label, entity.Index, _simulatedReadyFrames);
        }

        /// <summary>Poll: the fake init is "ready" once the simulated window has elapsed.</summary>
        protected override bool TryComplete(ISimulationView view, Entity entity)
        {
            if (!_deferFrame.TryGetValue(entity, out var start))
                return false;

            uint now = GetFrame(view);
            if (now - start >= (uint)_simulatedReadyFrames)
                return true;   // base PollPending → Complete: ack + OnCompleted cleanup

            // Not ready yet. If it is WAY past the window and still pending, the creator's abort has not
            // fired either (short-timeout spawn) or the entity is genuinely stuck — log once, keep waiting.
            if (now - start >= (uint)_simulatedReadyFrames + STALL_LOG_MARGIN_FRAMES && _stallLogged.Add(entity))
                Console.Error.WriteLine(
                    $"[SimulatedInitReadinessParticipant/{_label}] Entity {entity.Index}: still holding after " +
                    $"{now - start} frames (window {_simulatedReadyFrames}); awaiting creator disposal, not self-activating.");
            return false;
        }

        /// <summary>Clear per-entity state once acked.</summary>
        protected override void OnCompleted(ISimulationView? view, Entity entity, IEntityCommandBuffer cmd)
            => ClearState(entity);

        /// <summary>Clear per-entity state for a ghost torn down before it completed (e.g. creator abort).</summary>
        protected override void OnDestroyed(Entity entity) => ClearState(entity);

        /// <summary>
        /// CE-291: a reliable ghost NEVER self-force-activates on timeout (§3b.3) — it waits for its
        /// condition or the creator's <c>EntityMaster</c> dispose. Overriding to a no-op keeps the ghost in
        /// <see cref="EntityLifecycle.Constructing"/>, so a peer whose fake window outlives the creator's
        /// <c>ReliableInitTimeout</c> becomes the "stuck but participating" peer the abort leg needs.
        /// </summary>
        protected override void OnTimeout(ISimulationView view, Entity entity, IEntityCommandBuffer cmd, uint currentFrame)
        {
            // Deliberately do nothing: keep waiting. Cleanup happens on the creator's dispose (OnDestroyed).
        }

        private void ClearState(Entity entity)
        {
            _deferFrame.Remove(entity);
            _stallLogged.Remove(entity);
        }

        private static uint GetFrame(ISimulationView view) => view is EntityRepository r ? r.GlobalVersion : 0u;
    }
}

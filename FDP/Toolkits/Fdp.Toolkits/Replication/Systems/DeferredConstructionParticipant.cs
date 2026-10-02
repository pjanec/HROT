using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// Abstract base for a construction participant that may DEFER its
    /// <see cref="EntityLifecycleModule.AcknowledgeConstruction"/> until a condition
    /// clears — either a local readiness poll (navmesh tile loaded, model streamed,
    /// a synthetic test gate) or an external signal (a peer's Active ack, delivered
    /// through <see cref="NetworkGatewaySystem.ReceiveLifecycleStatus"/>).
    ///
    /// <para>It lifts the machinery <see cref="NetworkGatewaySystem"/> previously
    /// hand-rolled — register-with-ELM, a pending set with a per-entity start frame,
    /// force-ack on timeout, and destruction cleanup that still answers the
    /// <see cref="DestructionOrder"/> — so navmesh / model-load / synthetic
    /// participants do not each re-implement it (ruling 9). Barrier design
    /// <c>docs/DESIGN_Cross_Node_Construction_Barrier.md</c> §3a.5.</para>
    ///
    /// <para>Two completion modes: a <b>poll</b> subclass overrides
    /// <see cref="TryComplete"/> (called every frame for each pending entity); a
    /// <b>reactive</b> subclass (the gateway) leaves <see cref="TryComplete"/> false
    /// and calls <see cref="Complete"/> from its external signal handler.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public abstract class DeferredConstructionParticipant : IEcsModuleSystem
    {
        /// <summary>The ELM module id this participant acks under.</summary>
        protected readonly int _moduleId;

        /// <summary>The lifecycle module that drives construction/destruction.</summary>
        protected readonly EntityLifecycleModule _elm;

        private readonly int _timeoutFrames;

        // Entities this participant has deferred and not yet acked, with the frame they entered.
        private readonly HashSet<Entity> _pending = new();
        private readonly Dictionary<Entity, uint> _pendingStartFrame = new();
        // CE-289 (C3): optional per-entity timeout override (frames). Absent ⇒ the ctor default.
        private readonly Dictionary<Entity, int> _pendingTimeoutFrames = new();

        // Scratch buffers reused each tick to avoid per-frame allocation.
        private readonly List<Entity> _completeBuffer = new();
        private readonly List<Entity> _timedOutBuffer = new();

        /// <summary>Default force-ack timeout (5 s @ 60 Hz), matching the legacy gateway.</summary>
        public const int DEFAULT_TIMEOUT_FRAMES = 300;

        protected DeferredConstructionParticipant(int moduleId, EntityLifecycleModule elm, int timeoutFrames = -1)
        {
            _moduleId = moduleId;
            _elm = elm ?? throw new ArgumentNullException(nameof(elm));
            _timeoutFrames = timeoutFrames > 0 ? timeoutFrames : DEFAULT_TIMEOUT_FRAMES;

            // Register as a GLOBAL participant so ELM adds this module to every entity's
            // RemainingAcks. A subclass that participates only for specific blueprint types
            // additionally calls _elm.RegisterRequirement in its own ctor AND overrides
            // Participates to filter — see NavmeshReadinessParticipant / the synthetic proof.
            _elm.RegisterModule(_moduleId);
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            uint currentFrame = view is EntityRepository repo ? repo.GlobalVersion : 0u;
            var cmd = view.GetCommandBuffer();

            ProcessConstructionOrders(view, cmd, currentFrame);
            PollPending(view, cmd, currentFrame);
            ProcessDestructionOrders(view, cmd);
            CheckTimeouts(view, cmd, currentFrame);
        }

        /// <summary>CE-289 (C3): a subclass sets a per-entity timeout (frames) from its own state (e.g. the
        /// gateway reads the creator's <c>ReliableInitTimeout</c> off the stamped peer set). Call from
        /// <see cref="OnDeferred"/>. ≤0 keeps the ctor default.</summary>
        protected void SetPendingTimeout(Entity entity, int timeoutFrames)
        {
            if (timeoutFrames > 0)
                _pendingTimeoutFrames[entity] = timeoutFrames;
        }

        private void ProcessConstructionOrders(ISimulationView view, IEntityCommandBuffer cmd, uint currentFrame)
        {
            foreach (var evt in view.ReadEvents<ConstructionOrder>())
            {
                if (!Participates(view, evt.Entity, evt.BlueprintId))
                    continue;

                if (TryImmediateComplete(view, evt.Entity))
                {
                    // No wait needed for this entity — ack this frame.
                    _elm.AcknowledgeConstruction(evt.Entity, _moduleId, currentFrame, cmd);
                    OnCompleted(view, evt.Entity, cmd);
                    continue;
                }

                _pending.Add(evt.Entity);
                _pendingStartFrame[evt.Entity] = currentFrame;
                OnDeferred(view, evt.Entity);
            }
        }

        private void PollPending(ISimulationView view, IEntityCommandBuffer cmd, uint currentFrame)
        {
            if (_pending.Count == 0)
                return;

            _completeBuffer.Clear();
            foreach (var entity in _pending)
                if (TryComplete(view, entity))
                    _completeBuffer.Add(entity);

            foreach (var entity in _completeBuffer)
                Complete(entity, cmd, currentFrame);
        }

        /// <summary>
        /// Acknowledge construction for a deferred entity and clear its pending state.
        /// Idempotent: a second call for an entity already completed is a no-op. Called by
        /// the poll path and by reactive subclasses from their external signal handler.
        /// </summary>
        protected void Complete(Entity entity, IEntityCommandBuffer cmd, uint currentFrame)
        {
            if (!_pending.Remove(entity))
                return; // not pending (already completed, or never deferred)

            _pendingStartFrame.Remove(entity);
            _pendingTimeoutFrames.Remove(entity);
            _elm.AcknowledgeConstruction(entity, _moduleId, currentFrame, cmd);
            OnCompleted(view: null, entity, cmd);
        }

        private void CheckTimeouts(ISimulationView view, IEntityCommandBuffer cmd, uint currentFrame)
        {
            if (_pending.Count == 0)
                return;

            _timedOutBuffer.Clear();
            foreach (var kv in _pendingStartFrame)
            {
                int limit = _pendingTimeoutFrames.TryGetValue(kv.Key, out var perEntity) ? perEntity : _timeoutFrames;
                if (currentFrame - kv.Value > (uint)limit)
                    _timedOutBuffer.Add(kv.Key);
            }

            foreach (var entity in _timedOutBuffer)
                OnTimeout(view, entity, cmd, currentFrame);
        }

        private void ProcessDestructionOrders(ISimulationView view, IEntityCommandBuffer cmd)
        {
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                _pending.Remove(evt.Entity);
                _pendingStartFrame.Remove(evt.Entity);
                _pendingTimeoutFrames.Remove(evt.Entity);
                OnDestroyed(evt.Entity);

                cmd.PublishEvent(new DestructionAck
                {
                    Entity   = evt.Entity,
                    ModuleId = _moduleId,
                    Success  = true
                });
            }
        }

        /// <summary>True if this entity is currently deferred by this participant.</summary>
        protected bool IsPending(Entity entity) => _pending.Contains(entity);

        // ── Subclass hooks ──────────────────────────────────────────────────

        /// <summary>
        /// Does this participant take responsibility for the entity in this
        /// <see cref="ConstructionOrder"/>? A global waiter (the gateway) returns true for
        /// all; a per-type participant returns true only for its registered blueprint types.
        /// </summary>
        protected abstract bool Participates(ISimulationView view, Entity entity, long blueprintId);

        /// <summary>
        /// Can this entity be acked immediately (no wait)? Default true (behaves as a
        /// non-deferring participant). Return false to defer until <see cref="TryComplete"/>
        /// or an external <see cref="Complete"/>.
        /// </summary>
        protected virtual bool TryImmediateComplete(ISimulationView view, Entity entity) => true;

        /// <summary>Called once when an entity is deferred, to seed subclass state.</summary>
        protected virtual void OnDeferred(ISimulationView view, Entity entity) { }

        /// <summary>
        /// Poll hook for poll-mode subclasses: is the deferred entity now ready? Reactive
        /// subclasses (the gateway) leave this false and drive completion via
        /// <see cref="Complete"/>. <paramref name="view"/> is non-null on the poll path.
        /// </summary>
        protected virtual bool TryComplete(ISimulationView view, Entity entity) => false;

        /// <summary>Called after a successful ack (poll, external, or timeout). <paramref name="view"/> may be null.</summary>
        protected virtual void OnCompleted(ISimulationView? view, Entity entity, IEntityCommandBuffer cmd) { }

        /// <summary>Called when a pending entity is destroyed before completing.</summary>
        protected virtual void OnDestroyed(Entity entity) { }

        /// <summary>
        /// CE-289 (C3): the deferred entity's timeout expired. The DEFAULT is the legacy behaviour —
        /// force-ack via <see cref="Complete"/>, so a synthetic/poll participant that stalls still unblocks.
        /// ⭐ The creator gateway OVERRIDES this to ABORT instead (dispose the <c>EntityMaster</c> via a local
        /// teardown, and record <c>Failed(Timeout)</c>) — a reliable entity is never force-activated
        /// (DESIGN_Cross_Node_Construction_Barrier.md §3b.3). <paramref name="view"/> is the live world.
        /// </summary>
        protected virtual void OnTimeout(ISimulationView view, Entity entity, IEntityCommandBuffer cmd, uint currentFrame)
        {
            Console.Error.WriteLine(
                $"[{GetType().Name}] Entity {entity.Index}: force-ack after timeout.");
            Complete(entity, cmd, currentFrame);
        }
    }
}

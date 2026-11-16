using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8013</c> ② (SV-6, D-P4 / D-P6) — THE HELD LIFECYCLE PARTICIPANT: a split's child (<see cref="AggregateChild.Held"/> ≠ 0) stays
    /// <c>Constructing</c> — not simulated, not seen, not replicated as active — until the HANDOVER releases it (<c>Held = 0</c>), and only then is
    /// its construction acknowledged. ⭐ Registered per ENTITY (<see cref="EntityLifecycleModule.RegisterEntityRequirement"/>), so no other entity
    /// waits on it, and UNBOUNDED (<see cref="EntityLifecycleModule.WaitWithoutTimeout"/>): ⛔ the default lifecycle timeout DESTROYS a
    /// construction (SV-6: "a 30 s hold kills the men"). ⭐ A seek re-opens a held child's construction (<c>ResumeFromRestoredWorld</c>) and it is
    /// held again by the same rule. Composed by <c>EntityCreationPack</c> beside the creation path, so no node that can create a child lacks it.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class HeldChildParticipant : IEcsModuleSystem
    {
        /// <summary>The lifecycle module id this participant acks under (CE-8013; beside the obstacle bake's 0x7E41).</summary>
        public const int LifecycleModuleId = 0x7E81;

        private readonly EntityLifecycleModule _elm;
        private readonly EntityRepository _world;
        private readonly HashSet<Entity> _pending = new();
        private readonly List<Entity> _release = new();
        private int _epoch;

        /// <summary>Registers the per-entity rule and the unbounded wait on <paramref name="elm"/> — the participant and its rule come together.</summary>
        public HeldChildParticipant(EntityLifecycleModule elm, EntityRepository world)
        {
            _elm = elm ?? throw new ArgumentNullException(nameof(elm));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _elm.RegisterEntityRequirement(IsHeld, LifecycleModuleId);
            _elm.WaitWithoutTimeout(LifecycleModuleId);
        }

        /// <summary>Is <paramref name="e"/> a split's child not yet handed over?</summary>
        public bool IsHeld(Entity e)
            => _world.IsComponentTypeRegistered<AggregateChild>() && _world.IsAlive(e) && _world.HasComponent<AggregateChild>(e)
               && _world.GetComponentRO<AggregateChild>(e).Held != 0;

        /// <summary>Held children waiting for their handover (diagnostics, rails).</summary>
        public int PendingCount => _pending.Count;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch)) _pending.Clear();   // a reload: the old world's holds are gone
            uint frame = view is EntityRepository repo ? repo.GlobalVersion : 0u;
            var cmd = view.GetCommandBuffer();
            foreach (var evt in view.ReadEvents<ConstructionOrder>())
                if (IsHeld(evt.Entity)) _pending.Add(evt.Entity);
                else if (_elm.IsWaitingFor(evt.Entity, LifecycleModuleId)) _elm.AcknowledgeConstruction(evt.Entity, LifecycleModuleId, frame, cmd);   // released before we saw it
            foreach (var evt in view.ReadEvents<DestructionOrder>()) _pending.Remove(evt.Entity);
            if (_pending.Count == 0) return;
            _release.Clear();
            foreach (var e in _pending)
                if (!_world.IsAlive(e)) { _elm.AbandonConstruction(e); _release.Add(e); }   // destroyed while held (a cancelled split): nothing to ack, and the wait is unbounded
                else if (!IsHeld(e)) { _elm.AcknowledgeConstruction(e, LifecycleModuleId, frame, cmd); _release.Add(e); }
            for (int i = 0; i < _release.Count; i++) _pending.Remove(_release[i]);
        }
    }
}

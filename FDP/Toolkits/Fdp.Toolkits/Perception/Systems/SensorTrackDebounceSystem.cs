using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Muscle-tier sensor debounce system. Replaces <see cref="ThreatEvaluationSystem"/>
    /// on the SimHost node.
    ///
    /// <para><b>Responsibilities:</b>
    /// <list type="number">
    ///   <item>Consumes <see cref="TargetVisibleEvent"/>s from the module-private scoped bus
    ///     and records sightings in <see cref="SensorContactList"/> (raw, cognitively-neutral).</item>
    ///   <item>Evaluates hysteresis: a contact transitions from
    ///     <see cref="SensorContactState.Pending"/> / <see cref="SensorContactState.Lost"/>
    ///     to <see cref="SensorContactState.Acquired"/> when seen in the current tick,
    ///     and from <see cref="SensorContactState.Acquired"/> to <see cref="SensorContactState.Lost"/>
    ///     when the occlusion age exceeds <see cref="ContactHysteresis.TrackLostThresholdTicks"/>.</item>
    ///   <item>Publishes a <see cref="SensorTrackStateEvent"/> to the command buffer whenever
    ///     a contact transitions to <see cref="SensorContactState.Acquired"/> or
    ///     <see cref="SensorContactState.Lost"/>.  Inside <c>AutonomousPerceptionModule</c>
    ///     these events land on the module-private scoped bus and are then forwarded to the
    ///     global world bus so that <see cref="ActiveSensorTracksUpdateSystem"/> and the
    ///     DDS egress translator can consume them.</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Read-modify-write contract:</b>
    /// Reads <see cref="SensorContactList"/> from the SoD snapshot, modifies a local copy,
    /// then writes via <c>ecb.SetComponent</c> (or <c>ecb.AddComponent</c> on first encounter).
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Manual)]
    public class SensorTrackDebounceSystem : IEcsModuleSystem
    {
        // ⭐ CE-3032 — this tick's sightings GROUPED BY OBSERVER, built once. 🔴 Pass 1 used to scan every
        //   TargetVisibleEvent for every observer: observers × (observers × visible targets) — CUBIC. Measured
        //   (2026-10-04): 757 ms at 500 units for this system alone, past the module's 100 ms limit, so the circuit
        //   breaker opened and perception went dark. Reused across ticks; order within a group = event order.
        private readonly Dictionary<Entity, int> _groupOf = new();
        private readonly List<(Entity Observer, List<Entity> Targets)> _groups = new();
        private readonly Stack<List<Entity>> _pool = new();

        /// <inheritdoc/>
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            var ecb = view.GetCommandBuffer();
            uint currentTick = view.Tick;

            var visibleEvents = view.ReadEvents<TargetVisibleEvent>();
            GroupByObserver(visibleEvents);

            // ── Pass 1: update entities that already have SensorContactList ───────
            Span<(long TargetId, SensorTrackStatus State)> transitions = stackalloc (long, SensorTrackStatus)[PerceptionConstants.MaxTrackedTargets];
            // ⚠ CE-3037 — a perception SENSOR CHILD carries its own SensorContactList too, written by the EQS memory stage
            //   (SensorMemoryStage); this chain owns only the UNIT's list (one writer per component).
            var query = view.Query().With<SensorContactList>().Without<Fdp.Toolkit.Spatial.Eqs.EqsSensor>().Build();
            foreach (var entity in query)
            {
                ref readonly var listRO = ref view.GetComponentRO<SensorContactList>(entity);
                SensorContactList list = listRO;
                bool changed = false;

                // Apply all sightings for this observer.
                if (_groupOf.TryGetValue(entity, out int g))
                {
                    foreach (var target in _groups[g].Targets)
                    {
                        if (!view.IsAlive(target)) continue;

                        long targetId = (long)target.PackedValue;
                        SensorContactList.UpdateSighting(ref list, targetId, currentTick);
                        changed = true;
                    }
                }

                // Evaluate hysteresis transitions — ⭐ the ONE rule (ContactHysteresis), shared with the EQS memory stage.
                int n = ContactHysteresis.Apply(ref list, currentTick, transitions, out bool hysteresisChanged);
                changed |= hysteresisChanged;
                for (int t = 0; t < n; t++)
                {
                    var targetEntity = new Entity((ulong)transitions[t].TargetId);
                    float posX = 0f, posY = 0f;
                    if (transitions[t].State == SensorTrackStatus.Acquired &&
                        view.IsAlive(targetEntity) && view.HasComponent<SimTransform>(targetEntity))
                    {
                        ref readonly var tf = ref view.GetComponentRO<SimTransform>(targetEntity);
                        posX = tf.Position.X;
                        posY = tf.Position.Y;
                    }
                    ecb.PublishEvent(new SensorTrackStateEvent
                    {
                        Observer  = entity,
                        Target    = targetEntity,
                        State     = transitions[t].State,
                        PositionX = posX,
                        PositionY = posY,
                    });
                }

                if (changed)
                    ecb.SetComponent(entity, list);
            }

            // ── Pass 2: bootstrap SensorContactList for newly-seen observers ─────
            // ⭐ CE-3032 — ONE list per observer holding EVERY first-tick sighting. 🔴 It used to add one list PER
            //   sighting, each holding a single contact, so the last AddComponent won and an observer that saw
            //   several targets in its first tick kept only one (the others were announced Acquired, then never
            //   tracked and never Lost).
            foreach (var (observer, targets) in _groups)
            {
                if (!view.IsAlive(observer)) continue;
                if (view.HasComponent<SensorContactList>(observer)) continue; // handled in Pass 1

                var list = new SensorContactList();
                int before = 0;
                foreach (var target in targets)
                {
                    if (!view.IsAlive(target)) continue;
                    SensorContactList.UpdateSighting(ref list, (long)target.PackedValue, currentTick);
                    if (list.Count == before) continue; // a duplicate sighting, or the list is full
                    // age is 0, so immediately transition to Acquired.
                    list.State[before] = (byte)SensorContactState.Acquired;
                    before = list.Count;

                    // Emit Acquired event for the bootstrapped contact.
                    float posX = 0f, posY = 0f;
                    if (view.HasComponent<SimTransform>(target))
                    {
                        ref readonly var tf = ref view.GetComponentRO<SimTransform>(target);
                        posX = tf.Position.X;
                        posY = tf.Position.Y;
                    }
                    ecb.PublishEvent(new SensorTrackStateEvent
                    {
                        Observer  = observer,
                        Target    = target,
                        State     = SensorTrackStatus.Acquired,
                        PositionX = posX,
                        PositionY = posY,
                    });
                }
                if (list.Count > 0) ecb.AddComponent(observer, list);
            }
        }

        private void GroupByObserver(System.ReadOnlySpan<TargetVisibleEvent> events)
        {
            foreach (var (_, targets) in _groups) { targets.Clear(); _pool.Push(targets); }
            _groups.Clear();
            _groupOf.Clear();
            foreach (ref readonly var evt in events)
            {
                if (!_groupOf.TryGetValue(evt.Observer, out int g))
                {
                    g = _groups.Count;
                    _groupOf[evt.Observer] = g;
                    _groups.Add((evt.Observer, _pool.Count > 0 ? _pool.Pop() : new List<Entity>()));
                }
                _groups[g].Targets.Add(evt.Target);
            }
        }
    }
}

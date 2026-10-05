using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐⭐ <b>The memory stage — inside the EQS solver</b> (docs/DESIGN_Sensors_And_Doctrine.md §5.4, <c>CE-3037</c>).
    /// <para>Each perception sensor (a sensor child with a <see cref="SensorTag"/>) keeps its OWN
    /// <see cref="SensorContactList"/>: its sightings, debounced by <see cref="ContactHysteresis"/> — the same rule the
    /// visual chain uses. Each sensor runs at most once per tick, so each list has exactly one writer.</para>
    /// <para>⭐ What reaches the Brain is the UNIT's view: a <see cref="SensorTrackStateEvent"/> is published only where the
    /// union of the unit's acquired contacts over all its sensors (vision included since S5, <c>CE-3038</c>)
    /// CHANGED this tick. One sensor losing a target another still holds sends nothing.</para>
    /// </summary>
    public sealed class SensorMemoryStage
    {
        private readonly List<(Entity Sensor, Entity Unit, SensorContactList List, bool Existed)> _observed = new();
        private readonly Dictionary<Entity, int> _observedIndex = new();
        private readonly Dictionary<Entity, List<Entity>> _sensorsOfUnit = new();
        private readonly Stack<List<Entity>> _pool = new();
        private readonly List<Entity> _units = new();
        // ⭐ CE-3060 — per target, the OR of the KINDS of the unit's sensors that hold it (S7 design §6 G).
        private readonly Dictionary<long, byte> _before = new();
        private readonly Dictionary<long, byte> _after = new();
        private readonly List<long> _ordered = new();
        // ⭐ CE-3062 — this tick's anonymous sound contacts (an acoustic sensor's answers), published at Flush.
        private readonly List<SoundContactEvent> _heard = new();

        /// <summary>Transitions published by the last <see cref="Flush"/> (test hook / diagnostics).</summary>
        public int LastFlushTransitions { get; private set; }

        /// <summary>⭐ CE-3062 — sound contacts published by the last <see cref="Flush"/>.</summary>
        public int LastFlushHeard { get; private set; }

        private bool _enabled;

        /// <summary>Starts a solver tick. ⭐ Inert on a node that does not register the perception components.</summary>
        public void Begin(EntityRepository repo)
        {
            _observed.Clear();
            _observedIndex.Clear();
            _heard.Clear();
            _enabled = repo.IsComponentTypeRegistered<SensorContactList>() && repo.IsComponentTypeRegistered<SensorTag>();
        }

        /// <summary>True when <paramref name="sensor"/> is a perception sensor whose answers are sightings.</summary>
        public static bool IsPerceptionSensor(ISimulationView view, Entity sensor)
            => view.HasComponent<SensorTag>(sensor) && view.HasComponent<PartMetadata>(sensor);

        /// <summary>
        /// Records one evaluation of <paramref name="sensor"/>: every entity-shaped result is a sighting at
        /// <paramref name="tick"/>; then the hysteresis rule runs on the sensor's list.
        /// </summary>
        public unsafe void Observe(ISimulationView view, Entity sensor, uint tick, ReadOnlySpan<EqsResult> results)
        {
            if (!_enabled || !IsPerceptionSensor(view, sensor)) return;
            // ⭐ CE-3062 — an ACOUSTIC sensor's answers are anonymous estimates, not sightings: each becomes a SoundContactEvent
            //   for the unit (docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.1). The Brain's memory merges them (CE-3063).
            if (view.GetComponentRO<SensorTag>(sensor).Kind == SensorModality.Acoustic)
            {
                HearAll(view, sensor, results);
                return;
            }
            bool existed = view.HasComponent<SensorContactList>(sensor);
            var list = existed ? view.GetComponentRO<SensorContactList>(sensor) : default;
            foreach (ref readonly var r in results)
                if (r.EntityId > 0) SensorContactList.UpdateSighting(ref list, r.EntityId, tick);
            Span<(long, SensorTrackStatus)> ignored = stackalloc (long, SensorTrackStatus)[PerceptionConstants.MaxTrackedTargets];
            ContactHysteresis.Apply(ref list, tick, ignored, out _);
            Record(view, sensor, list, existed);
        }

        private void HearAll(ISimulationView view, Entity sensor, ReadOnlySpan<EqsResult> results)
        {
            var unit = view.GetComponentRO<PartMetadata>(sensor).ParentEntity;
            if (results.IsEmpty || !view.IsAlive(unit)) return;
            foreach (ref readonly var r in results)
            {
                if (r.EntityId != 0) continue;
                _heard.Add(new SoundContactEvent
                {
                    Observer = unit, X = r.PositionX, Y = r.PositionY, Z = r.PositionZ,
                    Radius   = r.Score,   // the radius the generator drew the error in (AcousticSensorGenerator)
                    Kind     = (byte)((r.Flags & AcousticPerception.KindMask) >> AcousticPerception.KindShift),
                    SourceClass = (byte)((r.Flags & AcousticPerception.ClassMask) >> AcousticPerception.ClassShift),
                });
            }
        }

        /// <summary>A sensor that stopped (suspended): it holds nothing any more.</summary>
        public void Clear(ISimulationView view, Entity sensor)
        {
            if (!_enabled || !IsPerceptionSensor(view, sensor) || !view.HasComponent<SensorContactList>(sensor)) return;
            if (view.GetComponentRO<SensorContactList>(sensor).Count == 0) return;
            Record(view, sensor, default, existed: true);
        }

        private void Record(ISimulationView view, Entity sensor, SensorContactList list, bool existed)
        {
            var unit = view.GetComponentRO<PartMetadata>(sensor).ParentEntity;
            if (_observedIndex.TryGetValue(sensor, out int at)) { _observed[at] = (sensor, unit, list, existed); return; }
            _observedIndex[sensor] = _observed.Count;
            _observed.Add((sensor, unit, list, existed));
        }

        /// <summary>
        /// Writes every observed list and publishes the UNIT-level transitions (union before vs after), in a deterministic
        /// order (units by first observation, targets ascending).
        /// </summary>
        public unsafe void Flush(ISimulationView view, IEntityCommandBuffer cmd)
        {
            LastFlushTransitions = 0;
            foreach (var h in _heard) cmd.PublishEvent(h);
            LastFlushHeard = _heard.Count;
            _heard.Clear();
            if (_observed.Count == 0) return;

            // Every perception sensor child, grouped by unit — once per flush.
            foreach (var l in _sensorsOfUnit.Values) { l.Clear(); _pool.Push(l); }
            _sensorsOfUnit.Clear();
            foreach (var e in view.Query().With<PartMetadata>().With<SensorContactList>().With<EqsSensor>().Build())
            {
                var unit = view.GetComponentRO<PartMetadata>(e).ParentEntity;
                if (!_sensorsOfUnit.TryGetValue(unit, out var l))
                    _sensorsOfUnit[unit] = l = _pool.Count > 0 ? _pool.Pop() : new List<Entity>();
                l.Add(e);
            }

            _units.Clear();
            foreach (var o in _observed)
                if (!_units.Contains(o.Unit)) _units.Add(o.Unit);

            foreach (var unit in _units)
            {
                if (!view.IsAlive(unit)) continue;
                _before.Clear();
                _after.Clear();

                if (_sensorsOfUnit.TryGetValue(unit, out var siblings))
                    foreach (var s in siblings)
                    {
                        ref readonly var old = ref view.GetComponentRO<SensorContactList>(s);
                        byte kind = KindOf(view, s);
                        AddAcquired(in old, kind, _before);
                        if (!_observedIndex.ContainsKey(s)) AddAcquired(in old, kind, _after);
                    }
                foreach (var o in _observed)
                    if (o.Unit == unit) { var l = o.List; AddAcquired(in l, KindOf(view, o.Sensor), _after); }

                Publish(view, cmd, unit, _after, _before, SensorTrackStatus.Acquired);
                Publish(view, cmd, unit, _before, _after, SensorTrackStatus.Lost);
            }

            foreach (var o in _observed)
            {
                if (!view.IsAlive(o.Sensor)) continue;
                if (o.Existed) cmd.SetComponent(o.Sensor, o.List);
                else cmd.AddComponent(o.Sensor, o.List);
            }
        }

        // Targets in `from` and not in `except`, ascending, published as `state`. ⭐ CE-3060 — an Acquired is also published for
        //   a target held before and after whose KINDS changed (heard, then also seen), so the Brain's track learns the new set.
        private void Publish(ISimulationView view, IEntityCommandBuffer cmd, Entity unit,
                             Dictionary<long, byte> from, Dictionary<long, byte> except, SensorTrackStatus state)
        {
            _ordered.Clear();
            foreach (var kv in from)
                if (!except.TryGetValue(kv.Key, out byte was)
                    || (state == SensorTrackStatus.Acquired && was != kv.Value)) _ordered.Add(kv.Key);
            _ordered.Sort();
            foreach (long id in _ordered)
            {
                var target = new Entity((ulong)id);
                float x = 0f, y = 0f;
                if (state == SensorTrackStatus.Acquired && view.IsAlive(target) && view.HasComponent<SimTransform>(target))
                {
                    var p = view.GetComponentRO<SimTransform>(target).Position;
                    x = p.X; y = p.Y;
                }
                cmd.PublishEvent(new SensorTrackStateEvent
                {
                    Observer = unit, Target = target, State = state, PositionX = x, PositionY = y,
                    Modality = (SensorModality)from[id],
                });
                LastFlushTransitions++;
            }
        }

        private static unsafe void AddAcquired(in SensorContactList list, byte kind, Dictionary<long, byte> into)
        {
            for (int i = 0; i < list.Count; i++)
                if (list.State[i] == (byte)SensorContactState.Acquired)
                    into[list.EntityIds[i]] = (byte)((into.TryGetValue(list.EntityIds[i], out byte k) ? k : 0) | kind);
        }

        // A sensor's kind; a perception sensor always has a SensorTag (IsPerceptionSensor), Visual as a guard.
        private static byte KindOf(ISimulationView view, Entity sensor)
            => view.HasComponent<SensorTag>(sensor) ? (byte)view.GetComponentRO<SensorTag>(sensor).Kind : (byte)SensorModality.Visual;
    }
}

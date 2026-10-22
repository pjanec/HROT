using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3072</c> B3 + B4 (R-213, B2) — the Brain half of the danger-area sensor: ⓪ hand the solver the unit's remembered
    /// contacts (<see cref="WriteContacts"/>, ⭐ <c>CE-3272</c>); ① APPLY each answer
    /// (<see cref="DangerAreaResultEvent"/>, epoch-checked) into the child's <see cref="DangerAreaCognitiveBuffer"/>; ② RATE
    /// every area EVERY tick from what the unit knows — the strongest remembered contact (<see cref="ThreatDanger.OfSlot"/>, so
    /// a killed one is 0, <c>CE-3080</c>) whose LAST-KNOWN position the SOLVER found has sight of the area
    /// (<see cref="DangerAreaDescriptor.SeenFrom"/>, ⭐ <c>CE-3272</c> / R-3018: the brain reads no terrain for it);
    /// ③ raise <see cref="SensorChangedEvent"/> edges on the next area ahead (<see cref="SensorChange.AreaAhead"/>,
    /// <see cref="SensorChange.AreaThreatened"/>, <see cref="SensorChange.AreaCleared"/>).
    /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// <para>⭐ Behaviours only READ the processed buffer (the user: "the commander's behavior does not care how the stuff is
    /// postprocessed"). The threat is never stored by the solver: it follows the Brain's memory between answers.</para>
    /// </summary>
    [SingleInstance]
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class DangerAreaSensorSystem : IEcsModuleSystem
    {
        /// <summary>The next area ahead counts as threatened from this rating…</summary>
        public const float ThreatenedAt = 0.5f;
        /// <summary>…and as cleared again below this one (hysteresis).</summary>
        public const float ClearedBelow = 0.4f;
        /// <summary>The eye height a contact sees from (m above its last-known position).</summary>
        public const float EyeHeight = 1.6f;
        /// <summary>The height of the point in the area that must be seen (m above its centre).</summary>
        public const float AreaHeight = 1.0f;
        /// <summary>⭐ <c>CE-3272</c> — a remembered contact must move this far (m) before the solver is handed the new point.</summary>
        public const float ContactMoveMetres = 3f;

        private Dictionary<Entity, (uint Feature, bool Threatened)> _last = new(), _next = new();
        private int _epoch;   // ⭐ CE-3294 — the world the state below describes (last step's areas, for the edges)

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch)) { _last.Clear(); _next.Clear(); }   // ⭐ CE-3294 — a reload's units come back under the same handles
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<DangerAreaCognitiveBuffer>() || !repo.IsComponentTypeRegistered<EqsSensor>()) return;
            WriteContacts(repo, view.GetCommandBuffer());
            Apply(repo, (float)view.Time);
            Rate(repo, view);
        }

        // ── ① the answers ──
        private static void Apply(EntityRepository repo, float now)
        {
            Span<DangerAreaDescriptor> previous = stackalloc DangerAreaDescriptor[8];   // CE-2224 — the answer being replaced
            foreach (var evt in repo.ReadManagedEvents<DangerAreaResultEvent>())
            {
                var child = evt.Observer.IsNull ? Resolve(repo, evt) : evt.Observer;
                if (child.IsNull || !repo.IsAlive(child)) continue;
                if (!repo.HasComponent<EqsSensor>(child) || !repo.HasComponent<DangerAreaCognitiveBuffer>(child)) continue;
                if (evt.Epoch != repo.GetComponentRO<EqsSensor>(child).Epoch) continue;   // a stale answer — for an older route

                // ⭐ CE-3081 (§10.7b A5) — keep only the kinds this sensor asked for (0 = the v1 crossings); the answer on the wire is
                //   the same for every sensor.
                var wanted = repo.IsComponentTypeRegistered<DangerAreaSensor>() && repo.HasComponent<DangerAreaSensor>(child)
                    ? repo.GetComponentRO<DangerAreaSensor>(child).Settings.Kinds : DangerAreaKinds.Crossings;
                ref var buffer = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(child);
                var span = buffer.GetSpanRW();
                int had = Math.Min(buffer.Count, span.Length);
                span.Slice(0, had).CopyTo(previous);
                int count = Math.Min(evt.Count, evt.Areas.Length);
                int n = 0;
                for (int i = 0; i < count && n < span.Length; i++)
                {
                    if (!DangerAreaKindsOf.Wants(wanted, evt.Areas[i].Kind)) continue;
                    span[n] = evt.Areas[i];
                    span[n].ThreatRating = 0f;   // rated below, from the Brain's memory
                    n++;
                }
                buffer.Count = n;
                DangerAreaCognitiveBuffer.KeepHandles(previous.Slice(0, had), span.Slice(0, n));   // CE-2224
                buffer.LastUpdateTick = evt.RefreshTick != 0 ? evt.RefreshTick : 1u;
                buffer.LastUpdateTimeSeconds = now;   // Q4 — what BecomesStale reads
            }
        }

        // The ONE wire-key rule (EqsSensorKey), as EqsResultUpdateSystem's local path matches.
        private static Entity Resolve(EntityRepository repo, DangerAreaResultEvent evt)
        {
            foreach (var candidate in repo.Query().With<EqsSensor>().With<DangerAreaCognitiveBuffer>().Build())
            {
                if (evt.ParentNetworkId == 0)
                {
                    if (candidate.Index == evt.LocalChildIndex) return candidate;
                    continue;
                }
                if (EqsSensorKey.Resolve(repo, candidate, out long net, out int index, out _) is EqsSensorKeyKind.Child or EqsSensorKeyKind.Legacy
                    && net == evt.ParentNetworkId && index == evt.LocalChildIndex)
                    return candidate;
            }
            return Entity.Null;
        }

        // ── ⓪ the contacts the solver judges ──
        /// <summary>
        /// ⭐ <c>CE-3272</c> (Q94 B, R-3018 — 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §5.3) — each danger-area
        /// sensor's <see cref="EqsContextPoints"/> = its unit's remembered contacts' LAST-KNOWN positions, slot for slot (the memory, or a
        /// body-less commander's squad pool), rewritten only when a slot moved more than <see cref="ContactMoveMetres"/> or the count
        /// changed — the config egress carries it to the solver, which answers <see cref="DangerAreaDescriptor.SeenFrom"/>. A sensor
        /// without the component gets it through <paramref name="cmd"/> (next frame).
        /// </summary>
        public static unsafe void WriteContacts(EntityRepository repo, Fdp.Interfaces.IEntityCommandBuffer cmd)
        {
            if (!repo.IsComponentTypeRegistered<EqsContextPoints>()) return;
            bool memory = repo.IsComponentTypeRegistered<TargetMemory>();
            bool pooled = repo.IsComponentTypeRegistered<SquadCognitiveState>();
            foreach (var child in repo.Query().With<DangerAreaCognitiveBuffer>().With<PartMetadata>().Build())
            {
                var unit = repo.GetComponentRO<PartMetadata>(child).ParentEntity;
                if (unit.IsNull || !repo.IsAlive(unit)) continue;
                var now = new EqsContextPoints();
                if (memory && repo.HasComponent<TargetMemory>(unit))
                {
                    ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(unit);
                    now.Count = Math.Min(mem.Count, EqsContextPoints.Capacity);
                    for (int i = 0; i < now.Count; i++) now.Set(i, new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]));
                }
                else if (pooled && repo.HasComponent<SquadCognitiveState>(unit))
                {
                    ref readonly var squad = ref repo.GetComponentRO<SquadCognitiveState>(unit);
                    var pool = PoolOf(in squad);
                    now.Count = Math.Min(Math.Min(squad.Contacts.Count, pool.Length), EqsContextPoints.Capacity);
                    for (int i = 0; i < now.Count; i++) now.Set(i, new Vector3(pool[i].PositionX, pool[i].PositionY, pool[i].PositionZ));
                }
                if (repo.HasComponent<EqsContextPoints>(child))
                {
                    ref var had = ref repo.GetComponentRW<EqsContextPoints>(child);
                    if (had.DiffersFrom(in now, ContactMoveMetres)) had = now;
                }
                else if (now.Count > 0)
                    cmd.AddComponent(child, now);
            }
        }

        private static ReadOnlySpan<SquadContact> PoolOf(in SquadCognitiveState squad)
            => System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);

        // ── ② the rating and ③ the edges ──
        private void Rate(EntityRepository repo, ISimulationView view)
        {
            bool memory = repo.IsComponentTypeRegistered<TargetMemory>();
            bool pooled = repo.IsComponentTypeRegistered<SquadCognitiveState>();
            bool tags = repo.IsComponentTypeRegistered<SensorTag>();
            _next.Clear();

            foreach (var child in repo.Query().With<DangerAreaCognitiveBuffer>().With<PartMetadata>().Build())
            {
                var unit = repo.GetComponentRO<PartMetadata>(child).ParentEntity;
                if (unit.IsNull || !repo.IsAlive(unit)) continue;

                ref var buffer = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(child);
                var span = buffer.GetSpanRW();
                for (int i = 0; i < buffer.Count && i < span.Length; i++)
                    span[i].ThreatRating = span[i].Kind == DangerAreaKind.GasCloud ? 1f   // ⭐ CE-3227 (§7.3d-B G-5) — the gas IS the threat
                        : memory && repo.HasComponent<TargetMemory>(unit)
                        ? ThreatOn(repo, unit, in span[i])
                        : pooled && repo.HasComponent<SquadCognitiveState>(unit)
                            ? ThreatOnFromPool(repo, unit, in span[i])   // ⭐ CE-2159 X-a — a body-less commander
                            : 0f;

                // ③ edges on the next area ahead (slot 0 — the solve keeps route order)
                uint feature = buffer.Count > 0 ? span[0].FeatureId : 0u;
                float threat = buffer.Count > 0 ? span[0].ThreatRating : 0f;
                bool had = _last.TryGetValue(child, out var before);
                bool threatened = had && before.Threatened && before.Feature == feature
                    ? threat >= ClearedBelow
                    : threat >= ThreatenedAt;
                _next[child] = (feature, threatened);
                if (!buffer.IsReady) continue;

                var kind = tags && repo.HasComponent<SensorTag>(child) ? repo.GetComponentRO<SensorTag>(child).Kind : SensorModality.DangerArea;
                if (!had || before.Feature != feature) Publish(view, unit, child, kind, SensorChange.AreaAhead);
                if (threatened && !(had && before.Threatened && before.Feature == feature)) Publish(view, unit, child, kind, SensorChange.AreaThreatened);
                else if (!threatened && had && before.Threatened) Publish(view, unit, child, kind, SensorChange.AreaCleared);
            }
            (_last, _next) = (_next, _last);
        }

        /// <summary>
        /// The threat on <paramref name="area"/> as <paramref name="unit"/> knows it: the strongest remembered contact whose
        /// last-known position sees the area — <c>danger × sight</c>, the sight being the solver's <see cref="DangerAreaDescriptor.SeenFrom"/>
        /// bit for that memory slot (⭐ <c>CE-3272</c>: no terrain is read here).
        /// </summary>
        public static unsafe float ThreatOn(EntityRepository repo, Entity unit, in DangerAreaDescriptor area)
        {
            ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(unit);
            float best = 0f;
            for (int i = 0; i < mem.Count && i < EqsContextPoints.Capacity; i++)
            {
                if ((area.SeenFrom & (1 << i)) == 0) continue;
                float danger = ThreatDanger.OfSlot(repo, unit, in mem, i);
                if (danger > best) best = danger;
            }
            return Math.Clamp(best, 0f, 1f);
        }

        /// <summary>
        /// ⭐ <c>CE-2159</c> X-a (Squad Tactics §7.7.6) — the threat on <paramref name="area"/> as a SQUAD knows it: a commander has no
        /// memory of its own, so its sensor is rated from the squad's merged contact pool — 1 when any contact still in the fight (or
        /// heard, which cannot be told dead) has sight of the area from where it was last placed (the solver's
        /// <see cref="DangerAreaDescriptor.SeenFrom"/>, ⭐ <c>CE-3272</c>), else 0.
        /// </summary>
        public static float ThreatOnFromPool(EntityRepository repo, Entity unit, in DangerAreaDescriptor area)
        {
            ref readonly var squad = ref repo.GetComponentRO<SquadCognitiveState>(unit);
            var pool = PoolOf(in squad);
            for (int i = 0; i < squad.Contacts.Count && i < pool.Length; i++)
            {
                if ((area.SeenFrom & (1 << i)) == 0) continue;
                var c = pool[i];
                bool heard = (c.Flags & SquadContact.AnonymousFlag) != 0 || c.EntityId <= 0;
                if (!heard && !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, new Entity((ulong)c.EntityId))) continue;
                return 1f;
            }
            return 0f;
        }

        private static void Publish(ISimulationView view, Entity unit, Entity sensor, SensorModality kind, SensorChange what)
            => view.GetCommandBuffer().PublishEvent(new SensorChangedEvent
            {
                Unit = unit, Sensor = sensor, Target = Entity.Null, Kind = kind, What = what,
            });
    }
}

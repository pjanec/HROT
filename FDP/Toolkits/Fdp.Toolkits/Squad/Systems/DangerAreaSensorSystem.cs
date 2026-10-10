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
    /// ⭐⭐ <c>CE-3072</c> B3 + B4 (R-213, B2) — the Brain half of the danger-area sensor: ① APPLY each answer
    /// (<see cref="DangerAreaResultEvent"/>, epoch-checked) into the child's <see cref="DangerAreaCognitiveBuffer"/>; ② RATE
    /// every area EVERY tick from what the unit knows — the strongest remembered contact (<see cref="ThreatDanger.OfSlot"/>, so
    /// a killed one is 0, <c>CE-3080</c>) whose LAST-KNOWN position has sight of the area (<see cref="TerrainWorld.SegmentBlocked"/>);
    /// ③ raise <see cref="SensorChangedEvent"/> edges on the next area ahead (<see cref="SensorChange.AreaAhead"/>,
    /// <see cref="SensorChange.AreaThreatened"/>, <see cref="SensorChange.AreaCleared"/>).
    /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// <para>⭐ Behaviours only READ the processed buffer (the user: "the commander's behavior does not care how the stuff is
    /// postprocessed"). The threat is never stored by the solver: it follows the Brain's memory between answers.</para>
    /// </summary>
    [SingleInstance]
    [UpdateInPhase(SystemPhase.Simulation)]
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

        private Dictionary<Entity, (uint Feature, bool Threatened)> _last = new(), _next = new();

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<DangerAreaCognitiveBuffer>() || !repo.IsComponentTypeRegistered<EqsSensor>()) return;
            Apply(repo, (float)view.Time);
            Rate(repo, view);
        }

        // ── ① the answers ──
        private static void Apply(EntityRepository repo, float now)
        {
            foreach (var evt in repo.Bus.ReadManaged<DangerAreaResultEvent>())
            {
                var child = evt.Observer.IsNull ? Resolve(repo, evt) : evt.Observer;
                if (child.IsNull || !repo.IsAlive(child)) continue;
                if (!repo.HasComponent<EqsSensor>(child) || !repo.HasComponent<DangerAreaCognitiveBuffer>(child)) continue;
                if (evt.Epoch != repo.GetComponentRO<EqsSensor>(child).Epoch) continue;   // a stale answer — for an older route

                ref var buffer = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(child);
                var span = buffer.GetSpanRW();
                int n = Math.Min(Math.Min(evt.Count, evt.Areas.Length), span.Length);
                for (int i = 0; i < n; i++)
                {
                    span[i] = evt.Areas[i];
                    span[i].ThreatRating = 0f;   // rated below, from the Brain's memory
                }
                buffer.Count = n;
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

        // ── ② the rating and ③ the edges ──
        private void Rate(EntityRepository repo, ISimulationView view)
        {
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(view);   // ⭐ CE-1035 Q2 — bound to this view's doors (R-219)
            bool memory = repo.IsComponentTypeRegistered<TargetMemory>();
            bool tags = repo.IsComponentTypeRegistered<SensorTag>();
            _next.Clear();

            foreach (var child in repo.Query().With<DangerAreaCognitiveBuffer>().With<PartMetadata>().Build())
            {
                var unit = repo.GetComponentRO<PartMetadata>(child).ParentEntity;
                if (unit.IsNull || !repo.IsAlive(unit)) continue;

                ref var buffer = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(child);
                var span = buffer.GetSpanRW();
                for (int i = 0; i < buffer.Count && i < span.Length; i++)
                    span[i].ThreatRating = memory && repo.HasComponent<TargetMemory>(unit)
                        ? ThreatOn(repo, unit, terrain, in span[i])
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
        /// last-known position sees the area — <c>danger × sight</c>, sight 1 when nothing blocks it (or the node has no
        /// terrain), else 0.
        /// </summary>
        public static unsafe float ThreatOn(EntityRepository repo, Entity unit, Fdp.Toolkit.World.IWorldQuery? terrain, in DangerAreaDescriptor area)
        {
            ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(unit);
            var target = area.Center + new Vector3(0f, 0f, AreaHeight);
            float best = 0f;
            for (int i = 0; i < mem.Count; i++)
            {
                float danger = ThreatDanger.OfSlot(repo, unit, in mem, i);
                if (danger <= best) continue;
                var eye = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i] + EyeHeight);
                if (terrain != null && terrain.SightBlocked(eye, target)) continue;
                best = danger;
            }
            return Math.Clamp(best, 0f, 1f);
        }

        private static void Publish(ISimulationView view, Entity unit, Entity sensor, SensorModality kind, SensorChange what)
            => view.GetCommandBuffer().PublishEvent(new SensorChangedEvent
            {
                Unit = unit, Sensor = sensor, Target = Entity.Null, Kind = kind, What = what,
            });
    }
}

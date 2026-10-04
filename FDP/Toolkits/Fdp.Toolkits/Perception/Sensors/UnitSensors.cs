using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐⭐ <b>How game AI finds and drives a unit's sensors</b> (R-185 L, R-187 N′; docs/DESIGN_Sensors_And_Doctrine.md §4).
    /// ⛔ AI never enumerates children or knows part ids — it asks by KIND.
    /// </summary>
    public static class UnitSensors
    {
        /// <summary>The unit's sensor of <paramref name="kind"/> (the lowest part id when it has several), or <see cref="Entity.Null"/>.</summary>
        public static Entity Of(ISimulationView view, Entity unit, SensorModality kind)
        {
            Entity best = Entity.Null;
            int bestPart = int.MaxValue;
            foreach (var e in view.Query().With<SensorTag>().With<PartMetadata>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(e);
                if (!meta.ParentEntity.Equals(unit) || view.GetComponentRO<SensorTag>(e).Kind != kind) continue;
                if (meta.InstanceId < bestPart) { best = e; bestPart = meta.InstanceId; }
            }
            return best;
        }

        /// <summary>The ranked results of the unit's <paramref name="kind"/> sensor (the EQS reader API: <c>IsReady</c>, <c>GetTop</c>, …).</summary>
        public static bool TryGetResults(ISimulationView view, Entity unit, SensorModality kind, out EqsCognitiveBuffer results)
        {
            var sensor = Of(view, unit, kind);
            if (sensor.IsNull || !view.HasComponent<EqsCognitiveBuffer>(sensor)) { results = default; return false; }
            results = view.GetComponentRO<EqsCognitiveBuffer>(sensor);
            return true;
        }

        /// <summary>
        /// ⭐ R-187 N′ — switch a sensor ON or OFF (an off sensor costs nothing on the Muscle). On a TKB sensor this is an
        /// OVERRIDE (it goes on the wire); <see cref="ClearOverride"/> returns it to its TKB default.
        /// </summary>
        public static void SetEnabled(EntityRepository repo, Entity sensor, bool enabled)
        {
            ref var s = ref repo.GetComponentRW<EqsSensor>(sensor);
            if (s.Suspended == !enabled) return;   // already so — no override, no wire sample
            s.Suspended = !enabled;
            s.Epoch = EqsChildSensor.NextEpoch(s.Epoch);
            if (IsTkb(repo, sensor) && !IsOverridden(repo, sensor)) SetPayload(repo, sensor, string.Empty);
        }

        /// <summary>
        /// ⭐ R-186 M′ — give a sensor a new per-kind config (the TKB's own record). On a TKB sensor this is an override.
        /// The template, radius and on/off follow <paramref name="config"/>; the epoch moves so older answers are dropped.
        /// </summary>
        public static void Configure(EntityRepository repo, Entity sensor, SensorEntryDto config)
        {
            ref var s = ref repo.GetComponentRW<EqsSensor>(sensor);
            var next = SensorChildFactory.SensorFor(config);
            s.BlueprintId  = next.BlueprintId;
            s.SearchRadius = next.SearchRadius;
            s.Suspended    = next.Suspended;
            s.Epoch        = EqsChildSensor.NextEpoch(s.Epoch);
            SetPayload(repo, sensor, SensorConfigCodec.Encode(config));
            if (!repo.HasComponent<SensorTag>(sensor) && repo.IsComponentTypeRegistered<SensorTag>())
                repo.AddComponent(sensor, new SensorTag { Kind = config.Kind });   // a behaviour-made sensor: UnitSensors.Of finds it by kind
            var defaultEntry = repo.HasManagedComponent<SensorCapability>(sensor)
                ? repo.GetManagedComponentRO<SensorCapability>(sensor).Default : config;
            SensorChildFactory.SetCapability(repo, sensor, defaultEntry, config);
        }

        /// <summary>⭐ R-187 N′ — a TKB sensor back to its TKB default (the override ends; one "default" sample goes on the wire).</summary>
        public static void ClearOverride(EntityRepository repo, Entity sensor)
        {
            if (!IsOverridden(repo, sensor)) return;
            repo.RemoveComponent<SensorConfigPayload>(sensor);
            if (!repo.HasManagedComponent<SensorCapability>(sensor)) return;
            var def = repo.GetManagedComponentRO<SensorCapability>(sensor).Default;
            ref var s = ref repo.GetComponentRW<EqsSensor>(sensor);
            var restored = SensorChildFactory.SensorFor(def);
            restored.Epoch        = EqsChildSensor.NextEpoch(s.Epoch);
            restored.ContextSlot0 = s.ContextSlot0;
            restored.ContextSlot1 = s.ContextSlot1;
            restored.ContextSlot2 = s.ContextSlot2;
            s = restored;
            SensorChildFactory.SetCapability(repo, sensor, def, def);
        }

        /// <summary>True when the sensor was built from the unit's TKB (part id ≥ <see cref="SensorChildFactory.FirstTkbPartId"/>).</summary>
        public static bool IsTkb(ISimulationView view, Entity sensor)
            => view.HasComponent<SensorTag>(sensor) && view.GetComponentRO<SensorTag>(sensor).FromTkb == 1;

        /// <summary>True when a TKB sensor carries an override (its wire sample is live).</summary>
        public static bool IsOverridden(ISimulationView view, Entity sensor)
            => view.HasManagedComponent<SensorConfigPayload>(sensor);

        private static void SetPayload(EntityRepository repo, Entity sensor, string json)
        {
            repo.RegisterManagedComponent<SensorConfigPayload>();   // idempotent
            repo.SetManagedComponent(sensor, new SensorConfigPayload { Kind = SensorConfigCodec.KindSensorEntry, Json = json });
        }
    }
}

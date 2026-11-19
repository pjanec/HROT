using System;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — <b>thermal as a sensor</b>: vision's own chain (<see cref="VisionBroadphase"/> range / FOV / other
    /// forces, the host's <see cref="ILosStrategy"/>) read with the THERMAL sensor's range and FOV, plus
    /// <see cref="SignatureFilterTest"/> — a target is seen only when its signature reaches the sensor's
    /// <c>MinSignature</c>. docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 F.
    /// </summary>
    public static class ThermalPerception
    {
        /// <summary>The template's asset id (a <c>SensorEntryDto.Template</c> for a thermal sensor).</summary>
        public const string AssetId = "3f0c1d7a-3061-4b6e-8d2a-5e1f0c9b7a61";

        public static readonly Guid AssetGuid = new(AssetId);

        public static uint BlueprintId => EqsTemplateRegistry.BlueprintIdOf(AssetGuid);

        public static EqsQueryTemplate Template(SpatialHashGrid grid, ILosStrategy sight) => new()
        {
            BlueprintId   = BlueprintId,
            Generator     = new VisualSensorGenerator(grid, Optics),
            FilterCheap   = new IEqsTest[] { new SignatureFilterTest(), new StrategySightTest(sight) },
            ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
            MaxCandidates = VisionBroadphase.MaxCandidatesPerObserver,
        };

        public static void Register(EqsTemplateRegistry registry, SpatialHashGrid grid, ILosStrategy sight)
            => registry.Register(AssetGuid, Template(grid, sight), "Thermal");

        /// <summary>
        /// ⭐ <c>CE-1985</c> (docs/designs/sensor-view/DESIGN_Sensor_View.md D-10) — THE thermal detection rule, shared by the sensor's
        /// <see cref="SignatureFilterTest"/> and the 3-D map's thermal view (its outlines of what the selected observer can see): a target's signature, 0..1 —
        /// its passive base dimmed by the crossover <paramref name="contrast"/> (<see cref="PerceptionWeather.BaseContrast"/>) plus its heat
        /// (<see cref="HeatOf"/>) over the rest. ⛔ One rule, never a copy in the view.
        /// </summary>
        public static float SignatureOf(ISimulationView view, Entity target, in ThermalState ts, float contrast, double now, bool sources = true, bool fires = true)
            => ts.BaseSignature * contrast + SignatureFilterTest.HeatOf(view, target, in ts, now, sources, fires) * (1f - ts.BaseSignature);

        /// <summary>Whether a <paramref name="signature"/> reaches a sensor's <paramref name="minSignature"/> (a zero signature never does).</summary>
        public static bool Reaches(float signature, float minSignature) => signature >= minSignature && signature > 0f;

        /// <summary>⭐ <c>CE-1985</c> — what a thermal <paramref name="sensor"/> (a unit's sensor part, or the unit itself) looks with: its <c>MinSignature</c>, its range
        /// (weather-capped at <paramref name="unit"/>, as <see cref="Optics"/>) and the crossover contrast there. False when it is not a thermal sensor.</summary>
        public static bool TryEyeOf(ISimulationView view, Entity sensor, Entity unit, out float minSignature, out float range, out float contrast)
        {
            minSignature = 0f; range = 0f; contrast = 1f;
            if (!view.HasManagedComponent<SensorCapability>(sensor) || view.GetManagedComponentRO<SensorCapability>(sensor).Current.Thermal is not { } th) return false;
            minSignature = th.MinSignature;
            range = th.Range;
            if (PerceptionWeather.TrySample(view, unit, out var weather))
            {
                range = PerceptionWeather.ThermalRange(range, in weather);
                contrast = PerceptionWeather.BaseContrast(in weather);
            }
            return range > 0f;
        }

        /// <summary>The (range, FOV cosine) a thermal sensor looks with — its current thermal parameters.</summary>
        internal static bool Optics(ISimulationView view, Entity sensor, Entity unit, out float range, out float fovCos)
        {
            if (view.HasManagedComponent<SensorCapability>(sensor)
                && view.GetManagedComponentRO<SensorCapability>(sensor).Current.Thermal is { } th)
            {
                range  = th.Range;
                fovCos = MathF.Cos(th.FieldOfViewDegrees * 0.5f * MathF.PI / 180f);
                // ⭐ CE-3216 S2-3 (R-3017) — no light term; fog caps it at twice the visibility
                if (PerceptionWeather.TrySample(view, unit, out var weather))
                    range = PerceptionWeather.ThermalRange(range, in weather);
                return range > 0f;
            }
            range = 0f; fovCos = 1f;
            return false;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3061</c> — keeps a candidate only when its <see cref="ThermalState.Signature"/> reaches the sensor's
    /// <c>MinSignature</c>. A target with no <see cref="ThermalState"/> gives off nothing (absent signature ⇒ cold).
    /// </summary>
    public sealed class SignatureFilterTest : IEqsTest
    {
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            float min = view.HasManagedComponent<SensorCapability>(observer)
                        && view.GetManagedComponentRO<SensorCapability>(observer).Current.Thermal is { } th ? th.MinSignature : 0f;
            // ⭐ CE-3216 S2-3 (R-3017) — the crossover dims only the PASSIVE base signature (a cold hull against its background); the heat
            //   term (an engine, a weapon fired, a body) is unchanged. Sampled once, at the observer's unit; 1 without a clock.
            var unit = EqsContext.Self(view, observer, sensor);
            float contrast = PerceptionWeather.TrySample(view, unit, out var weather) ? PerceptionWeather.BaseContrast(in weather) : 1f;
            // ⭐ CE-4658 (thermal drivers TD-10) — the heat is COMPUTED from the replicated drivers at this instant (ThermalDriversOps), never integrated here
            double now = view is EntityRepository clockRepo && clockRepo.HasSingletonUnmanaged<GlobalTime>() ? clockRepo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0.0;
            bool sources = view is not EntityRepository sr || sr.IsComponentTypeRegistered<HeatSource>();
            bool fires = view is not EntityRepository fr || fr.IsComponentTypeRegistered<FireState>();
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId <= 0) continue;
                var target = new Entity((ulong)c.EntityId);
                bool hot = false;
                if (view.IsAlive(target) && view.HasComponent<ThermalState>(target))
                {
                    ref readonly var ts = ref view.GetComponentRO<ThermalState>(target);
                    hot = ThermalPerception.Reaches(ThermalPerception.SignatureOf(view, target, in ts, contrast, now, sources, fires), min);
                }
                if (!hot) c.EntityId = -1;
            }
        }

        /// <summary>
        /// ⭐ <c>CE-4658</c> — the hottest of the target's sources at <paramref name="now"/>, 0..1: its engine or body (<see cref="ThermalDriversOps.SourceHeat"/> from the
        /// replicated <see cref="HeatSource"/> and the speed this node knows, <see cref="Fdp.Toolkit.Motion.Locomotions.SpeedOf"/>), its hottest barrel
        /// (<see cref="ThermalState.BarrelHeat"/> until CE-4659 computes it from the last burst) and a fire on it (<see cref="FireState"/>).
        /// ⚠ A target whose state says it moves but whose speed this node cannot read (a person on the node that moves him) counts at full load.
        /// </summary>
        internal static float HeatOf(ISimulationView view, Entity target, in ThermalState ts, double now, bool sources, bool fires)
        {
            var d = UnitThermalDrivers.Of(view, target, in ts, now, sources, fires);
            float heat = MathF.Max(d.Barrel01, d.Fire01);
            if (d.HasSource) heat = MathF.Max(heat, ThermalDriversOps.SourceHeat(in ts, in d.Source, d.Speed, now, d.DiedAt));
            return Math.Clamp(heat, 0f, 1f);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-1985</c> (docs/designs/sensor-view/DESIGN_Sensor_View.md D-7, §5b) — ONE gathering of a unit's heat drivers at <c>now</c> from what this node has
    /// replicated, for the AI's thermal sensor (<see cref="ThermalPerception.HeatOf"/>) AND the 3-D map's thermal view (<c>ThermalScene.Unit</c>) — ⛔ never two
    /// copies of which speed, which death time, which barrel. ⭐ Moved here from <c>HeatOf</c> unchanged (CE-4658 / CE-4659 / CE-4661 rules).
    /// </summary>
    public readonly struct UnitThermalDrivers
    {
        public readonly bool HasSource;
        public readonly HeatSource Source;
        /// <summary>The speed the source's load reads (m/s); a target the state says moves but whose speed this node cannot read counts at full load.</summary>
        public readonly float Speed;
        /// <summary>A dead BODY's time of death (<c>Health.StateSince</c>); NaN = alive or an engine.</summary>
        public readonly double DiedAt;
        /// <summary>The hottest barrel, 0..1 (a carried station weapon's, or the unit's own mounts' from their last shot).</summary>
        public readonly float Barrel01;
        /// <summary>The strongest burning fire spot on it, 0..1.</summary>
        public readonly float Fire01;

        private UnitThermalDrivers(bool hasSource, in HeatSource source, float speed, double diedAt, float barrel, float fire)
        {
            HasSource = hasSource; Source = source; Speed = speed; DiedAt = diedAt; Barrel01 = barrel; Fire01 = fire;
        }

        /// <summary>The drivers of <paramref name="target"/> at <paramref name="now"/>; <paramref name="sources"/> / <paramref name="fires"/> false skip those
        /// (a view without the components registered).</summary>
        public static UnitThermalDrivers Of(ISimulationView view, Entity target, in ThermalState ts, double now, bool sources = true, bool fires = true)
        {
            float barrel = ts.BarrelHeat;   // a station weapon carried (ThermalHeatSystem)
            // ⭐ CE-4659 — the unit's own barrels, computed from each mount's last shot (WeaponAmmo, replicated to every node)
            if ((view is not EntityRepository ar || ar.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponAmmo>())
                && view.HasComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>(target))
                barrel = MathF.Max(barrel, view.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponAmmo>(target).HottestBarrelAt(now));
            bool has = false; HeatSource hs = default; float speed = 0f; double diedAt = double.NaN;
            if (sources && (view is not EntityRepository sr || sr.IsComponentTypeRegistered<HeatSource>()) && view.HasComponent<HeatSource>(target))
            {
                has = true;
                hs = view.GetComponentRO<HeatSource>(target);
                speed = Fdp.Toolkit.Motion.Locomotions.SpeedOf(view, target);
                if (speed < HeatSourceSystem.MoveOffSpeed && hs.State is HeatSourceState.Running or HeatSourceState.Moving)
                    speed = ts.ReferenceSpeed > 0f ? ts.ReferenceSpeed : 5f;
                // ⭐ CE-4661 (TD-7) — a dead body cools from its time of death (Health.StateSince, replicated)
                if (!hs.IsEngine && (view is not EntityRepository hr || hr.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>())
                    && view.HasComponent<Fdp.Toolkit.Combat.Components.Health>(target)
                    && view.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(target) is { Current: <= 0f } hp) diedAt = hp.StateSince;
            }
            float fire = 0f;
            if (fires && (view is not EntityRepository fr || fr.IsComponentTypeRegistered<FireState>()) && view.HasComponent<FireState>(target))
                fire = view.GetComponentRO<FireState>(target).Intensity01(now);
            return new UnitThermalDrivers(has, in hs, speed, diedAt, barrel, fire);
        }
    }
}

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
                    float signature = ts.BaseSignature * contrast + HeatOf(view, target, in ts, now, sources, fires) * (1f - ts.BaseSignature);
                    hot = signature >= min && signature > 0f;
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
            float heat = ts.BarrelHeat;   // a station weapon carried (ThermalHeatSystem)
            // ⭐ CE-4659 — the unit's own barrels, computed from each mount's last shot (WeaponAmmo, replicated to every node)
            if ((view is not EntityRepository ar || ar.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponAmmo>())
                && view.HasComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>(target))
                heat = MathF.Max(heat, view.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponAmmo>(target).HottestBarrelAt(now));
            if (sources && view.HasComponent<HeatSource>(target))
            {
                ref readonly var hs = ref view.GetComponentRO<HeatSource>(target);
                float speed = Fdp.Toolkit.Motion.Locomotions.SpeedOf(view, target);
                if (speed < HeatSourceSystem.MoveOffSpeed && hs.State is HeatSourceState.Running or HeatSourceState.Moving)
                    speed = ts.ReferenceSpeed > 0f ? ts.ReferenceSpeed : 5f;
                // ⭐ CE-4661 (TD-7) — a dead body cools from its time of death (Health.StateSince, replicated)
                double diedAt = double.NaN;
                if (!hs.IsEngine && (view is not EntityRepository hr || hr.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>())
                    && view.HasComponent<Fdp.Toolkit.Combat.Components.Health>(target)
                    && view.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(target) is { Current: <= 0f } hp) diedAt = hp.StateSince;
                heat = MathF.Max(heat, ThermalDriversOps.SourceHeat(in ts, in hs, speed, now, diedAt));
            }
            if (fires && view.HasComponent<FireState>(target))
                heat = MathF.Max(heat, view.GetComponentRO<FireState>(target).Intensity01(now));
            return Math.Clamp(heat, 0f, 1f);
        }
    }
}

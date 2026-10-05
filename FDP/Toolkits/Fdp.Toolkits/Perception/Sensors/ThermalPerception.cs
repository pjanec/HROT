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
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId <= 0) continue;
                var target = new Entity((ulong)c.EntityId);
                bool hot = view.IsAlive(target) && view.HasComponent<ThermalState>(target)
                        && view.GetComponentRO<ThermalState>(target).Signature >= min
                        && view.GetComponentRO<ThermalState>(target).Signature > 0f;
                if (!hot) c.EntityId = -1L;
            }
        }
    }
}

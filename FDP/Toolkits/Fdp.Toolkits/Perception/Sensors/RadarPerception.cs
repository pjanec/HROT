using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Perception.Radar;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐⭐ <c>CE-4617</c> (B-1; docs/designs/air-attack/DESIGN_Radar_SAM.md RS-2, RS-2a, RV-3, RV-12) — <b>a radar as a sensor</b>. Thermal's
    /// shape (§6 F of the thermal design) with its OWN candidate generator: it walks the AIRCRAFT only — never vision's nearest-32 cap (ground
    /// units would crowd a far helicopter out) and never the terrain-fitted grid (an aircraft beyond the map's edge is still seen). Then
    /// <see cref="RadarSignatureFilter"/> (above the clutter floor, cross-section at range) and the host's line of sight from the radar's eye
    /// (the carrier's sight height = its mast, RV-17) — with NO acquisition dwell (that models an eye finding a target; a radar acquires in a
    /// sweep, through the ordinary track hysteresis). It runs only while the carrier's replicated <see cref="RadarEmission"/> is on (RS-10).
    /// Contacts reach memory tagged <see cref="SensorModality.Radar"/> through the sensor child's tag, like every sense.
    /// </summary>
    public static class RadarPerception
    {
        /// <summary>The template's asset id (a <c>SensorEntryDto.Template</c> for a radar).</summary>
        public const string AssetId = "c4617a0b-5e1a-4d2b-9c07-7ad4a2b01b17";

        public static readonly Guid AssetGuid = new(AssetId);

        public static uint BlueprintId => EqsTemplateRegistry.BlueprintIdOf(AssetGuid);

        /// <summary>The cross-section a radar's stated range is measured against (m²).</summary>
        public const float ReferenceRcsM2 = 10f;

        /// <summary>Below this height above the ground an aircraft is lost in the clutter (m) — a pop-up attacker's way in (RS-6).</summary>
        public const float ClutterFloorM = 15f;

        /// <summary>The farthest a radar reaches as a multiple of its stated range — the biggest cross-section we carry (a C-130, 40 m²) is
        /// ~1.41×; the generator gathers to this and the filter decides per target.</summary>
        public const float MaxRangeFactor = 1.5f;

        public static EqsQueryTemplate Template(Func<ISimulationView, Fdp.Toolkit.World.IWorldQuery?> world, ILosStrategy sight) => new()
        {
            BlueprintId   = BlueprintId,
            Generator     = new RadarSensorGenerator(),
            FilterCheap   = new IEqsTest[] { new RadarSignatureFilter(world), new StrategySightTest(sight, acquisitionDelay: false) },
            ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
            MaxCandidates = VisionBroadphase.MaxCandidatesPerObserver,
        };

        public static void Register(EqsTemplateRegistry registry, Func<ISimulationView, Fdp.Toolkit.World.IWorldQuery?> world, ILosStrategy sight)
            => registry.Register(AssetGuid, Template(world, sight), "Radar");

        /// <summary>A listed radar entry (the carriage, <c>NedTkbBuilder</c>).</summary>
        public static SensorEntryDto Entry(float rangeM) => new()
        {
            Kind         = SensorModality.Radar,
            Template     = AssetGuid,
            SearchRadius = rangeM * MaxRangeFactor,
            Radar        = new RadarSensorDto { Range = rangeM, FieldOfViewDegrees = 360f },
        };

        /// <summary>How far a radar of stated <paramref name="radarRange"/> sees a <paramref name="rcsM2"/> target: range ∝ RCS^¼.</summary>
        public static float DetectionRange(float radarRange, float rcsM2)
            => rcsM2 <= 0f ? 0f : radarRange * MathF.Pow(rcsM2 / ReferenceRcsM2, 0.25f);

        /// <summary>The radar's stated range and FOV cosine — only while its carrier radiates (RS-10).</summary>
        internal static bool Optics(ISimulationView view, Entity sensor, Entity unit, out float range, out float fovCos)
        {
            range = 0f; fovCos = -1f;
            if (!RadarEmissions.IsOn(view, unit)) return false;
            if (!view.HasManagedComponent<SensorCapability>(sensor) || view.GetManagedComponentRO<SensorCapability>(sensor).Current.Radar is not { } r)
                return false;
            range  = r.Range;
            fovCos = r.FieldOfViewDegrees >= 360f ? -1f : MathF.Cos(r.FieldOfViewDegrees * 0.5f * MathF.PI / 180f);
            return range > 0f;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4617</c> (RV-3) — the radar's candidates: the AIRCRAFT of another force within the stated range × <see cref="RadarPerception.MaxRangeFactor"/>
    /// and the radar's arc. The airborne list is built ONCE per tick per calling thread (the solver's workers each keep their own), from a
    /// cached query — no allocation in the steady state.
    /// </summary>
    public sealed class RadarSensorGenerator : IEqsGenerator
    {
        private sealed class AirList
        {
            public ISimulationView? View;
            public uint Tick;
            public EntityQuery? Query;
            public ISimulationView? QueryView;
            public readonly List<(Entity E, Vector3 P, ForceId F)> Items = new();
        }

        private readonly System.Threading.ThreadLocal<AirList> _air = new(() => new AirList());

        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var unit = EqsContext.Self(view, observer, sensor);
            if (unit.IsNull || !view.IsAlive(unit) || !view.HasComponent<SimTransform>(unit) || !view.HasComponent<EntityInfo>(unit)) return 0;
            if (!RadarPerception.Optics(view, observer, unit, out float range, out float fovCos)) return 0;

            var air = Airborne(view);
            var from = view.GetComponentRO<SimTransform>(unit).Position;
            var force = view.GetComponentRO<EntityInfo>(unit).ForceId;
            Vector2 forward = default;
            if (fovCos > -1f)
            {
                var f3 = Vector3.Transform(Vector3.UnitX, view.GetComponentRO<SimTransform>(unit).Rotation);
                forward = Vector2.Normalize(new Vector2(f3.X, f3.Y));
            }
            float reach = range * RadarPerception.MaxRangeFactor, reachSq = reach * reach;
            int n = 0;
            for (int i = 0; i < air.Count && n < candidates.Length; i++)
            {
                var (e, p, f) = air[i];
                if (f == force || e.Index == unit.Index) continue;
                var d = p - from;
                if (d.LengthSquared() > reachSq) continue;
                if (fovCos > -1f)
                {
                    var flat = new Vector2(d.X, d.Y);
                    float len = flat.Length();
                    if (len > 1e-3f && Vector2.Dot(forward, flat / len) < fovCos) continue;
                }
                candidates[n++] = new EqsResult { EntityId = (long)e.PackedValue, PositionX = p.X, PositionY = p.Y, PositionZ = p.Z };
            }
            return n;
        }

        /// <summary>The live aircraft on <paramref name="view"/> this tick (position, force).</summary>
        private List<(Entity E, Vector3 P, ForceId F)> Airborne(ISimulationView view)
        {
            var a = _air.Value!;
            if (ReferenceEquals(a.View, view) && a.Tick == view.Tick) return a.Items;
            a.View = view; a.Tick = view.Tick;
            a.Items.Clear();
            if (!ReferenceEquals(a.QueryView, view)) { a.Query = view.Query().With<SimTransform>().With<EntityInfo>().Build(); a.QueryView = view; }
            foreach (var e in a.Query!)
            {
                if (!Fdp.Toolkit.Motion.Locomotions.IsAircraft(view, e)) continue;
                a.Items.Add((e, view.GetComponentRO<SimTransform>(e).Position, view.GetComponentRO<EntityInfo>(e).ForceId));
            }
            return a.Items;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4617</c> (RS-2) — keeps an aircraft only when it is ABOVE the clutter floor (<see cref="RadarPerception.ClutterFloorM"/> over the
    /// ground under it — beyond the terrain the world answers its own edge rule) and within the range its cross-section allows
    /// (<see cref="RadarPerception.DetectionRange"/>; no <see cref="RadarSignature"/> ⇒ the 10 m² reference).
    /// </summary>
    public sealed class RadarSignatureFilter : IEqsTest
    {
        private readonly Func<ISimulationView, Fdp.Toolkit.World.IWorldQuery?> _world;
        // ⭐ the world query is asked for once per tick per calling thread (the line of sight's own rule, BeginBatch) — a source builds a new
        //   one per call (TerrainWorldQuery.ForView), so asking per batch allocated per radar per tick
        private sealed class WorldMark { public ISimulationView? For; public uint Tick; public Fdp.Toolkit.World.IWorldQuery? Query; }
        private readonly System.Threading.ThreadLocal<WorldMark> _mark = new(() => new WorldMark());

        public RadarSignatureFilter(Func<ISimulationView, Fdp.Toolkit.World.IWorldQuery?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

        private Fdp.Toolkit.World.IWorldQuery? WorldOf(ISimulationView view)
        {
            var m = _mark.Value!;
            if (!ReferenceEquals(m.For, view) || m.Tick != view.Tick) { m.For = view; m.Tick = view.Tick; m.Query = _world(view); }
            return m.Query;
        }

        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var unit = EqsContext.Self(view, observer, sensor);
            float range = view.HasManagedComponent<SensorCapability>(observer)
                          && view.GetManagedComponentRO<SensorCapability>(observer).Current.Radar is { } r ? r.Range : 0f;
            bool ok = range > 0f && !unit.IsNull && view.IsAlive(unit) && view.HasComponent<SimTransform>(unit);
            var from = ok ? view.GetComponentRO<SimTransform>(unit).Position : default;
            var world = ok ? WorldOf(view) : null;
            bool signatures = view is not EntityRepository repo || repo.IsComponentTypeRegistered<RadarSignature>();
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId <= 0) continue;
                var target = new Entity((ulong)c.EntityId);
                if (!ok || !view.IsAlive(target) || !view.HasComponent<SimTransform>(target)) { c.EntityId = -1; continue; }
                var p = view.GetComponentRO<SimTransform>(target).Position;
                float ground = world?.GroundHeightAt(p.X, p.Y) ?? 0f;
                if (p.Z - ground < RadarPerception.ClutterFloorM) { c.EntityId = -1; continue; }
                float rcs = signatures && view.HasComponent<RadarSignature>(target) ? view.GetComponentRO<RadarSignature>(target).RcsM2 : RadarPerception.ReferenceRcsM2;
                float reach = RadarPerception.DetectionRange(range, rcs);
                if (Vector3.DistanceSquared(from, p) > reach * reach) c.EntityId = -1;
            }
        }
    }
}

using System;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐⭐ <b>Vision as a sensor</b> (docs/DESIGN_Sensors_And_Doctrine.md §5.5, <c>CE-3038</c>, R-185). The visual template is
    /// the old perception chain's OWN steps — <see cref="VisionBroadphase"/> (range, FOV, other forces, nearest 32) then
    /// the host's <see cref="ILosStrategy"/> — run by the EQS solver on a unit's visual sensor child, so its sightings
    /// go through the memory stage like every other perception sensor.
    /// <para>⚠ The template needs the host's perception grid and sight strategy, so it is not a discoverable
    /// <c>[EqsTemplate]</c>: the host that owns both registers it (<see cref="Register"/>).</para>
    /// </summary>
    public static class VisualPerception
    {
        /// <summary>The template's asset id; its BlueprintId is the canonical hash of it.</summary>
        public const string AssetId = "6b1f0c52-3038-4c1e-9a55-7d0e5e3a0b11";

        /// <summary><see cref="AssetId"/> as a Guid (a <see cref="SensorEntryDto.Template"/>).</summary>
        public static readonly Guid AssetGuid = new(AssetId);

        /// <summary>The template's BlueprintId.</summary>
        public static uint BlueprintId => EqsTemplateRegistry.BlueprintIdOf(AssetGuid);

        /// <summary>The visual template over <paramref name="grid"/> (the perception grid) and <paramref name="sight"/>.</summary>
        public static EqsQueryTemplate Template(SpatialHashGrid grid, ILosStrategy sight) => new()
        {
            BlueprintId   = BlueprintId,
            Generator     = new VisualSensorGenerator(grid),
            FilterCheap   = new IEqsTest[] { new StrategySightTest(sight) },
            ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
            MaxCandidates = VisionBroadphase.MaxCandidatesPerObserver,
        };

        /// <summary>Registers the visual template in <paramref name="registry"/> (idempotent: a re-register replaces it).</summary>
        public static void Register(EqsTemplateRegistry registry, SpatialHashGrid grid, ILosStrategy sight)
            => registry.Register(AssetGuid, Template(grid, sight), "Visual");

        /// <summary>
        /// ⭐ The IMPLICIT visual entry of a TKB that lists no sensors but has a vision range (§4: existing TKB data keeps
        /// working with no migration). Its range is the search radius; the solver reads the live receptor anyway.
        /// </summary>
        public static SensorEntryDto ImplicitEntry(SensorCapabilitiesDto dto) => new()
        {
            Kind         = SensorModality.Visual,
            Template     = AssetGuid,
            SearchRadius = dto.VisionRange,
            Visual       = new VisualSensorDto { Range = dto.VisionRange, FieldOfViewDegrees = dto.FieldOfViewDegrees },
        };

        /// <summary>The (range, FOV cosine) a visual sensor looks with: the unit's receptor for the implicit sensor (or when
        /// the sensor carries no visual record), else its capability's current visual record.</summary>
        internal static bool Optics(ISimulationView view, Entity sensor, Entity unit, out float range, out float fovCos)
        {
            if (!NakedEyeOptics(view, sensor, unit, out range, out fovCos)) return false;
            // ⭐ CE-3234 (civilians B-12, HI-4) — binoculars up: further and narrower (vision only; a thermal sight keeps its own)
            if (Fdp.Toolkit.Combat.HeldItems.BinocularsUp(view, unit))
            {
                range *= Fdp.Toolkit.Combat.HeldItems.BinocularsRangeFactor;
                fovCos = MathF.Max(fovCos, MathF.Cos(Fdp.Toolkit.Combat.HeldItems.BinocularsFovDegrees * 0.5f * MathF.PI / 180f));
            }
            // ⭐ CE-3254 (BR-9, DA-13) — a scoped weapon aimed: the same rule through the scope
            else if (Fdp.Toolkit.Combat.HeldItems.ScopeUp(view, unit, out float magnification, out float scopeFov))
            {
                range *= magnification;
                fovCos = MathF.Max(fovCos, MathF.Cos(scopeFov * 0.5f * MathF.PI / 180f));
            }
            return true;
        }

        private static bool NakedEyeOptics(ISimulationView view, Entity sensor, Entity unit, out float range, out float fovCos)
        {
            bool useCapability = !(view.HasComponent<SensorTag>(sensor) && view.GetComponentRO<SensorTag>(sensor).Implicit == 1)
                && view.HasManagedComponent<SensorCapability>(sensor)
                && view.GetManagedComponentRO<SensorCapability>(sensor).Current.Visual != null;
            if (useCapability)
            {
                var v = view.GetManagedComponentRO<SensorCapability>(sensor).Current.Visual!;
                range  = v.Range;
                fovCos = MathF.Cos(v.FieldOfViewDegrees * 0.5f * MathF.PI / 180f);
                return true;
            }
            if (view.HasComponent<PerceptionReceptor>(unit))
            {
                ref readonly var r = ref view.GetComponentRO<PerceptionReceptor>(unit);
                range  = r.VisionRange;
                fovCos = r.FieldOfViewCos;
                return range > 0f;
            }
            range = 0f; fovCos = 1f;
            return false;
        }
    }

    /// <summary>
    /// The visual sensor's candidates: what its UNIT can look at (<see cref="VisionBroadphase"/>). The coarse index is
    /// rebuilt once per solver tick, not per sensor.
    /// </summary>
    public sealed class VisualSensorGenerator : IEqsGenerator
    {
        /// <summary>The (range, FOV cosine) a sensor looks with — vision's by default; ⭐ CE-3061 thermal passes its own.</summary>
        public delegate bool OpticsFn(ISimulationView view, Entity sensor, Entity unit, out float range, out float fovCos);

        private readonly SpatialHashGrid _grid;
        private readonly OpticsFn _optics;
        private readonly BroadphaseCache _broadphase = new();   // ⭐ CE-1055 — one per calling thread (the solver, the map's gizmo)

        public VisualSensorGenerator(SpatialHashGrid grid, OpticsFn? optics = null)
        {
            _grid   = grid;
            _optics = optics ?? VisualPerception.Optics;
        }

        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var unit = EqsContext.Self(view, observer, sensor);
            if (unit.IsNull || !view.IsAlive(unit)) return 0;
            if (!_optics(view, observer, unit, out float range, out float fovCos)) return 0;

            var broadphase = _broadphase.For(view, _grid);

            int n = 0;
            foreach (var (_, target) in broadphase.Select(view, unit, range, fovCos))
            {
                if (n == candidates.Length) break;
                var p = view.GetComponentRO<SimTransform>(target).Position;
                candidates[n++] = new EqsResult { EntityId = (long)target.PackedValue, PositionX = p.X, PositionY = p.Y, PositionZ = p.Z };
            }
            return n;
        }
    }

    /// <summary>
    /// Keeps the candidates the sensor's UNIT can see through the host's <see cref="ILosStrategy"/> — the same test, eye
    /// height and posture the old perception chain used. The strategy gathers its colliders once per solver tick.
    /// </summary>
    public sealed class StrategySightTest : IEqsTest, IEqsCostWeight
    {
        private readonly ILosStrategy _strategy;
        // ⭐ CE-1160 — which batch THIS THREAD's strategy holds (the strategy's batch is per thread too): the EQS solver's background thread
        //   and the map's EQS verdict on the draw thread call this one instance (CE-1055's rule, now for the filter).
        private sealed class BatchMark { public ISimulationView? For; public uint Tick; }
        private readonly System.Threading.ThreadLocal<BatchMark> _mark = new(() => new BatchMark());

        public StrategySightTest(ILosStrategy strategy) => _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Sight;

        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var unit = EqsContext.Self(view, observer, sensor);
            bool unitOk = !unit.IsNull && view.IsAlive(unit) && view.HasComponent<SimTransform>(unit);
            var mark = _mark.Value!;
            if (unitOk && (!ReferenceEquals(view, mark.For) || view.Tick != mark.Tick))
            {
                _strategy.BeginBatch(view);
                mark.For  = view;
                mark.Tick = view.Tick;
            }
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId <= 0) continue;
                var target = new Entity((ulong)c.EntityId);
                bool visible = unitOk && view.IsAlive(target) && view.HasComponent<SimTransform>(target)
                            && _strategy.IsVisible(view, unit, target);
                if (!visible) c.EntityId = -1L;
            }
        }
    }
}

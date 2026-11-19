using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐⭐ <c>CE-1987</c> (R-1058; docs/DESIGN_Thermal_And_Acoustic_Sensing.md §9, C-2 / C-5) — the thermal BACKGROUND of a world for one frame: the apparent °C of
    /// every thermal class (<see cref="ThermalScene"/>, flat surfaces, at the world's weather), what lies under a point (<see cref="GroundSurfaces"/>) and the
    /// sky for a flyer. <see cref="Prepare"/> runs SERIALLY once per frame (the EQS solve's serial start; a map host's view); the parallel sensor filters then only
    /// READ it (<see cref="Of"/> · <see cref="BackgroundC"/>) — no shared mutable state in the parallel phase. 0 B per frame once warm.
    /// <para>⚠ One weather sample per world (the global one, at the origin): a weather REGION's own temperatures do not move the background yet.</para>
    /// </summary>
    [NotState("derived each frame from the weather and the static terrain")]
    public sealed class ThermalBackground
    {
        private static readonly ConditionalWeakTable<ISimulationView, ThermalBackground> s_of = new();

        /// <summary>A target this far above its ground (m) is seen against the SKY (a flyer), else against the surface under it.</summary>
        public const float FlyerHeightM = 20f;

        /// <summary>The date a world with no scenario clock is lit by (noon, midsummer, 50°N) — a test world; a scenario always has a clock.</summary>
        public static readonly DateTime NoClockUtc = new(2026, 6, 21, 11, 0, 0, DateTimeKind.Utc);

        private readonly float[] _flat = new float[ThermalScene.ClassCount], _gain = new float[ThermalScene.ClassCount], _eps = new float[ThermalScene.ClassCount];
        private readonly float[] _apparent = new float[ThermalScene.ClassCount];
        private readonly ThermalClassId[] _byGround = new ThermalClassId[8];
        private ThermalScene.DayForcing _forcing;
        private DateTime _forcingDay;
        private double _forcingLat = double.NaN, _forcingLon = double.NaN;
        private float _forcingCut = float.NaN;
        private TerrainMaterialLibrary? _classesFrom;
        private SurfaceClassGrid? _grid;
        private Fdp.Toolkit.World.IWorldQuery? _query;

        /// <summary>The sim time it was prepared for (NaN: never).</summary>
        public double PreparedAt { get; private set; } = double.NaN;
        public float AirC { get; private set; } = 15f;
        public float SkyC { get; private set; }
        /// <summary>A vehicle hull's apparent °C (painted metal) — the surface an engine's heat sits on.</summary>
        public float HullC { get; private set; } = 15f;

        /// <summary>The background prepared for <paramref name="view"/> this frame; null when none was.</summary>
        public static ThermalBackground? Of(ISimulationView view) => s_of.TryGetValue(view, out var b) ? b : null;

        /// <summary>The apparent °C of <paramref name="c"/> (a flat surface) this frame.</summary>
        public float ApparentC(ThermalClassId c) => _apparent[(int)c];

        /// <summary>
        /// SERIAL, once per frame: the class temperatures at the world's weather now, the surface grid, the ground query. A second call at the same sim time is
        /// free. Returns the background.
        /// </summary>
        public static ThermalBackground Prepare(EntityRepository world)
        {
            var b = s_of.GetValue(world, _ => new ThermalBackground());
            double now = Fdp.Toolkit.Time.SimClock.TotalTimeOf(world);
            if (b.PreparedAt == now) return b;
            b.PreparedAt = now;
            b._grid = GroundSurfaces.Of(world);
            b._query = Fdp.Toolkit.World.WorldQuery.Of(world);
            var terrain = world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;
            b.MapGround(terrain?.Materials ?? TerrainMaterialLibrary.Shared);

            double lat = 0, lon = 0;
            WeatherSample s;
            if (ScenarioTime.TryClock(world, out _))
            {
                var utc = ScenarioTime.Utc(world);
                s = WeatherField.At(world, new Vector3(0f, 0f, 1.7f), utc, 0f);
                if (world.HasSingletonManaged<IGeographicTransform>() && world.GetSingletonManaged<IGeographicTransform>() is { HasFrame: true } geo)
                    (lat, lon, _) = geo.ToGeodetic(Vector3.Zero);
            }
            else
            {
                lat = 50.0; lon = 14.4;
                var sun = SolarPosition.Of(NoClockUtc, lat, lon);
                s = new WeatherSample { Utc = NoClockUtc, TemperatureC = 20f, GroundTemperatureC = 20f, DiurnalPhase = 1f, SunElevationDeg = sun.ElevationDeg, SunAzimuthDeg = sun.AzimuthDeg, VisibilityM = float.PositiveInfinity };
            }
            float cut = ThermalScene.CloudCut(s.CloudOctasAbove);
            if (s.Utc.Date != b._forcingDay || lat != b._forcingLat || lon != b._forcingLon || cut != b._forcingCut)
            {
                b._forcing = ThermalScene.DayForcing.Of(s.Utc, lat, lon, cut);   // once per sim day and place
                b._forcingDay = s.Utc.Date; b._forcingLat = lat; b._forcingLon = lon; b._forcingCut = cut;
            }
            ThermalScene.ClassTemperatures(in s, in b._forcing, b._flat, b._gain, b._eps);
            b.SkyC = ThermalScene.SkyC(in s);
            b.AirC = s.TemperatureC;
            for (int i = 0; i < ThermalScene.ClassCount; i++) b._apparent[i] = ThermalScene.Apparent(b._flat[i], b._eps[i], b.SkyC);
            b.HullC = b._apparent[(int)ThermalClassId.PaintedMetal];
            return b;
        }

        /// <summary>The surface classes of the grid, by the terrain's own <c>surfaceThermalClasses</c> (R-5065): bare ground · a field · scrub · a wood · water; a road is asphalt.</summary>
        private void MapGround(TerrainMaterialLibrary materials)
        {
            if (ReferenceEquals(materials, _classesFrom)) return;
            _classesFrom = materials;
            _byGround[(int)GroundClass.Open] = ThermalScene.FromName(materials.SurfaceThermalClass("ground") ?? "soil-dry");
            _byGround[(int)GroundClass.Field] = ThermalScene.FromName(materials.SurfaceThermalClass("open") ?? "grass");
            _byGround[(int)GroundClass.Scrub] = ThermalScene.FromName(materials.SurfaceThermalClass("scrub") ?? "grass");
            _byGround[(int)GroundClass.Forest] = ThermalScene.FromName(materials.SurfaceThermalClass("forest") ?? "dense-vegetation");
            _byGround[(int)GroundClass.Water] = ThermalScene.FromName(materials.SurfaceThermalClass("water") ?? "water");
            _byGround[(int)GroundClass.Road] = ThermalClassId.Asphalt;
        }

        /// <summary>The thermal class of the ground at (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public ThermalClassId GroundClassAt(float x, float y)
            => _byGround[(int)(_grid?.ClassAt(x, y) ?? GroundClass.Open)];

        /// <summary>
        /// ⭐ C-2 — the apparent °C a target at <paramref name="at"/> is seen against: the sky when it flies (more than <see cref="FlyerHeightM"/> above its ground),
        /// else the surface under it. Read-only — safe from the parallel filters.
        /// </summary>
        public float BackgroundC(Vector3 at)
        {
            float ground = _query?.GroundHeightAt(at.X, at.Y) ?? 0f;
            if (float.IsFinite(ground) && at.Z - ground > FlyerHeightM) return SkyC;
            return _apparent[(int)GroundClassAt(at.X, at.Y)];
        }
    }
}

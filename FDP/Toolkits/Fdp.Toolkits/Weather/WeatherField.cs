using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Weather
{
    /// <summary>Daylight band from the sun's elevation (the standard twilight limits, §4).</summary>
    public enum LightBand : byte { Night = 0, AstronomicalTwilight = 1, NauticalTwilight = 2, CivilTwilight = 3, Day = 4 }

    /// <summary>⭐ CE-3216 S1 (§6.3) — up to <see cref="WeatherConditions.MaxCloudLayers"/> cloud layers, as authored.</summary>
    public unsafe struct CloudDeck
    {
        public byte Count;
        private fixed float _base[WeatherConditions.MaxCloudLayers];
        private fixed float _thickness[WeatherConditions.MaxCloudLayers];
        private fixed byte _octas[WeatherConditions.MaxCloudLayers];
        private fixed byte _kind[WeatherConditions.MaxCloudLayers];

        /// <summary>The deck of <paramref name="w"/>.</summary>
        public static CloudDeck Of(WeatherConditions w)
        {
            var d = new CloudDeck { Count = (byte)Math.Min((int)w.CloudCount, WeatherConditions.MaxCloudLayers) };
            for (int i = 0; i < d.Count; i++)
            {
                d._base[i] = w.CloudBaseM[i]; d._thickness[i] = w.CloudThicknessM[i]; d._octas[i] = w.CloudOctas[i]; d._kind[i] = w.CloudKind[i];
            }
            return d;
        }

        /// <summary>Layer <paramref name="i"/>: base (m MSL), thickness (m), cover (octas, and its aviation band) and kind.</summary>
        public readonly (float BaseM, float ThicknessM, byte Octas, CloudCover Cover, CloudKind Kind) Layer(int i)
        {
            if ((uint)i >= Count) throw new ArgumentOutOfRangeException(nameof(i));
            fixed (float* b = _base) fixed (float* t = _thickness) fixed (byte* o = _octas) fixed (byte* k = _kind)
                return (b[i], t[i], o[i], CloudCovers.Of(o[i]), (CloudKind)k[i]);
        }
    }

    /// <summary>⭐ CE-3216 S1 — the weather at one point and moment: everything §4 derives, the one answer every consumer reads.</summary>
    public struct WeatherSample
    {
        public DateTime Utc;
        /// <summary>Air temperature at the point, °C (the diurnal swing and the lapse rate applied).</summary>
        public float TemperatureC;
        /// <summary>Air temperature at the ground below the point, °C.</summary>
        public float GroundTemperatureC;
        public float PressureHpa;
        /// <summary>Air density, kg/m³.</summary>
        public float DensityKgM3;
        public float RelativeHumidity;
        /// <summary>The wind at the point's height, m/s, the direction it blows TOWARD (X east, Y north).</summary>
        public Vector2 Wind;
        public float WindGustMps;
        /// <summary>Horizontal visibility at the point, m (inside a fog layer: the fog's).</summary>
        public float VisibilityM;
        /// <summary>Extinction coefficient σ = 3.912 / V, per metre (Koschmieder).</summary>
        public float ExtinctionPerM;
        /// <summary>Total sky cover, octas (0…8) — every layer, as seen from the ground (the day–night swing reads it).</summary>
        public float CloudOctas;
        /// <summary>⭐ §6.3 — the cover of the layers whose TOP is above the point, octas: what shades the point (the irradiance and the light
        /// read it). Above an overcast deck it is 0.</summary>
        public float CloudOctasAbove;
        /// <summary>⭐ §6.3 — the cloud layers at the point, for a renderer: the STRONGEST contributing weather's deck (W-2: a deck is a
        /// category; the cover numbers above are blended).</summary>
        public CloudDeck Clouds;
        /// <summary>Lowest base (m MSL) of a layer of ≥ 5 octas; +∞ when none.</summary>
        public float CeilingM;
        public PrecipitationKind Precipitation;
        public float PrecipitationMmPerHour;
        public double SunElevationDeg;
        public double SunAzimuthDeg;
        /// <summary>Solar irradiance on a horizontal surface, W/m².</summary>
        public float IrradianceWm2;
        public LightBand Light;
        /// <summary>Light relative to a clear noon, 0…1 (cloud-dimmed).</summary>
        public float RelativeLight;
        /// <summary>⭐ <c>CE-3216</c> S2 (R-3017) — where the day is in the ground temperature's swing, −1…1: ±1 at the day's extremes, 0
        /// where it crosses its daily mean (the thermal crossover, dawn and dusk).</summary>
        public float DiurnalPhase;
    }

    /// <summary>
    /// ⭐ CE-3216 S1 (R-3012; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md §1, §3 W-1/W-2, §4) — THE weather at any point and moment:
    /// a PURE function of replicated data (the World Environment's global weather and clock, every Weather Region) and the node's synced
    /// sim time. ⛔ No system, no state: a replay or a rewind reproduces it by construction, and there is no host on which it "never
    /// ticks". Allocation-free (R-220): a parachute asks every frame.
    /// </summary>
    public static class WeatherField
    {
        /// <summary>More regions than this overlapping one point are ignored (the weakest).</summary>
        public const int MaxContributors = 16;

        /// <summary>Clear-sky transmittance τ (§4).</summary>
        public const float ClearSkyTransmittance = 0.75f;
        /// <summary>The solar constant, W/m².</summary>
        public const float SolarConstant = 1361f;
        /// <summary>Roughness length for the surface wind's log profile, m (open terrain).</summary>
        public const float RoughnessM = 0.03f;

        private const float GasConstantDryAir = 287.05f;
        private const float Gravity = 9.80665f;

        // ── W-1: one region's weight at a point ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ W-1 — a region's weight at <paramref name="p"/>: along the ray from the centroid <paramref name="c"/> through p to the polygon's
        /// boundary b, <c>t = |p − c| / (|b − c| + feather)</c> and <c>w = smoothstep(1 − t)</c> — 1 at the centroid, 0 at the edge (or
        /// <paramref name="featherM"/> past it), smooth between. <paramref name="worldPolygon"/> is in world XY.
        /// </summary>
        public static float RegionWeight<TPoly>(TPoly worldPolygon, Vector2 c, float featherM, Vector2 p) where TPoly : IReadOnlyList<Vector2>
        {
            if (worldPolygon == null || worldPolygon.Count < 3) return 0f;
            var d = p - c;
            float dist = d.Length();
            if (dist < 1e-4f) return Contains(worldPolygon, c) ? 1f : 0f;
            var dir = d / dist;
            float reach = BoundaryDistance(worldPolygon, c, dir);
            if (float.IsInfinity(reach)) return 0f;   // the centroid is outside its own polygon on this ray: nothing to blend from
            float t = dist / (reach + MathF.Max(0f, featherM));
            return Smoothstep(1f - t);
        }

        /// <summary>The distance from <paramref name="c"/> along <paramref name="dir"/> to the nearest polygon edge (∞ when the ray misses).</summary>
        public static float BoundaryDistance<TPoly>(TPoly poly, Vector2 c, Vector2 dir) where TPoly : IReadOnlyList<Vector2>
        {
            float best = float.PositiveInfinity;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                var e = b - a;
                float den = Cross(dir, e);
                if (MathF.Abs(den) < 1e-9f) continue;            // parallel
                var ac = a - c;
                float s = Cross(ac, e) / den;                     // along the ray
                float u = Cross(ac, dir) / den;                   // along the edge
                if (s > 1e-5f && u >= 0f && u <= 1f && s < best) best = s;
            }
            return best;
        }

        /// <summary>The area centroid of a polygon (its vertex mean when degenerate).</summary>
        public static Vector2 Centroid<TPoly>(TPoly poly) where TPoly : IReadOnlyList<Vector2>
        {
            float a2 = 0f; float cx = 0f, cy = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                float cr = p.X * q.Y - q.X * p.Y;
                a2 += cr; cx += (p.X + q.X) * cr; cy += (p.Y + q.Y) * cr;
            }
            if (MathF.Abs(a2) < 1e-6f)
            {
                var m = Vector2.Zero;
                for (int i = 0; i < n; i++) m += poly[i];
                return n > 0 ? m / n : Vector2.Zero;
            }
            return new Vector2(cx / (3f * a2), cy / (3f * a2));
        }

        /// <summary>Even-odd point-in-polygon (the same rule as <c>PolygonMath.Contains</c>, generic so a view over an outline is not boxed).</summary>
        public static bool Contains<TPoly>(TPoly poly, Vector2 p) where TPoly : IReadOnlyList<Vector2>
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        public static float Smoothstep(float x)
        {
            x = Math.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // ── W-2: who contributes at a point ─────────────────────────────────────────────────────────────────────────────

        /// <summary>One weather contributing at a point, with its final weight (after W-2).</summary>
        public readonly record struct Contribution(Entity Source, float Weight, WeatherConditions Weather);

        /// <summary>
        /// ⭐ W-2 — every weather contributing at <paramref name="p"/>: each Weather Region by its W-1 weight, ADDING where they overlap; the
        /// global weather fills what is left to 1 (<c>g = max(0, 1 − Σw)</c>); past 1 the regions normalise among themselves (g = 0).
        /// The weights written to <paramref name="into"/> sum to 1. Returns the count (≥ 1: the global weather is always there).
        /// </summary>
        public static int Contributors(EntityRepository world, Vector2 p, Span<Contribution> into)
        {
            int n = 0;
            float sum = 0f;
            if (world.IsComponentTypeRegistered<WeatherConditions>() && world.IsComponentTypeRegistered<SimTransform>())
            {
                foreach (var e in world.Query().With<WeatherConditions>().With<SimTransform>().Build())
                {
                    if (n >= into.Length - 1) break;   // keep a slot for the global weather
                    if (!TryOutline(world, e, out var outline)) continue;
                    var origin = world.GetComponentRO<SimTransform>(e).Position;
                    var poly = new OffsetPolygon(outline, new Vector2(origin.X, origin.Y));
                    ref readonly var w = ref world.GetComponentRO<WeatherConditions>(e);
                    float weight = RegionWeight(poly, Centroid(poly), w.FeatherM, p);
                    if (weight <= 0f) continue;
                    into[n++] = new Contribution(e, weight, w);
                    sum += weight;
                }
            }
            float g = MathF.Max(0f, 1f - sum);
            if (sum > 1f)
                for (int i = 0; i < n; i++) into[i] = into[i] with { Weight = into[i].Weight / sum };
            var global = GlobalOf(world, out var env);
            into[n++] = new Contribution(env, g, global);
            return n;
        }

        /// <summary>The GLOBAL weather — the World Environment's (the C-3 built-in when it has none, or before it has arrived).</summary>
        public static WeatherConditions GlobalOf(EntityRepository world, out Entity environment)
        {
            environment = Entity.Null;
            if (world.IsComponentTypeRegistered<WeatherConditions>() && world.IsComponentTypeRegistered<ScenarioClock>())
                foreach (var e in world.Query().With<WeatherConditions>().With<ScenarioClock>().WithLifecycle(EntityLifecycle.All).Build())
                {
                    environment = e;
                    return world.GetComponentRO<WeatherConditions>(e);
                }
            return WeatherConditions.Standard;
        }

        /// <summary>The area outline of <paramref name="e"/> — any managed component implementing <see cref="IAreaOutline"/> under the
        /// <c>EditablePolyline</c> id (the one area shape every map tool authors and every node replicates).</summary>
        public static bool TryOutline(EntityRepository world, Entity e, out IAreaOutline outline)
        {
            outline = null!;
            if (!world.GetComponentMask(e.Index).IsSet(GlobalComponentIds.EditablePolyline)) return false;
            if (world.GetManagedComponentByTypeId(e, GlobalComponentIds.EditablePolyline) is not IAreaOutline o || o.Points.Count < 3) return false;
            outline = o;
            return true;
        }

        // ── the derived quantities (§4) ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>The wind at <paramref name="point"/> (m/s, toward; the height is the point's Z above sea level) — the light-weight
        /// read the parachute and the release point make (W-5).</summary>
        public static Vector2 WindAt(EntityRepository world, Vector3 point) => WindAt(world, point, GroundZ(world, point));

        /// <summary>⭐ <c>CE-3258</c> (CE-2287, R-2037) — the wind at <paramref name="point"/> with the ground under it GIVEN
        /// (<paramref name="ground"/>, m above sea level): a caller on the brain passes a replicated height (the entity's own Z) instead of
        /// reading the terrain.</summary>
        public static Vector2 WindAt(EntityRepository world, Vector3 point, float ground)
        {
            Span<Contribution> c = stackalloc Contribution[MaxContributors];
            int n = Contributors(world, new Vector2(point.X, point.Y), c);
            var wind = Vector2.Zero;
            for (int i = 0; i < n; i++)
                if (c[i].Weight > 0f) wind += c[i].Weight * WindOf(c[i].Weather, point.Z, ground, out _);
            return wind;
        }

        /// <summary>⭐ THE weather at <paramref name="point"/> now (§4).</summary>
        public static WeatherSample At(EntityRepository world, Vector3 point) => At(world, point, ScenarioTime.Utc(world));

        /// <summary>The weather at <paramref name="point"/> at <paramref name="utc"/>.</summary>
        public static WeatherSample At(EntityRepository world, Vector3 point, DateTime utc) => At(world, point, utc, GroundZ(world, point));

        /// <summary>⭐ <c>CE-3258</c> (CE-2287, R-2037) — the weather at <paramref name="point"/> with the ground under it GIVEN
        /// (<paramref name="ground"/>): a caller on the brain passes a replicated height instead of reading the terrain.</summary>
        public static WeatherSample At(EntityRepository world, Vector3 point, DateTime utc, float ground)
        {
            Span<Contribution> c = stackalloc Contribution[MaxContributors];
            int n = Contributors(world, new Vector2(point.X, point.Y), c);

            // the sun, from the point's latitude / longitude
            double lat = 0, lon = 0;
            if (world.HasSingletonManaged<IGeographicTransform>() && world.GetSingletonManaged<IGeographicTransform>() is { } geo)
                (lat, lon, _) = geo.ToGeodetic(point);
            var sun = SolarPosition.Of(utc, lat, lon);

            // ⭐ W-2 — scalars linearly, the wind as a vector, extinction (not visibility) linearly, categories by the strongest
            float mean = 0, lapse = 0, slp = 0, rh = 0, gust = 0, sigma = 0, octas = 0, octasAbove = 0, precipRate = 0;
            var deck = default(CloudDeck);
            var wind = Vector2.Zero;
            float strongest = -1f; float ceiling = float.PositiveInfinity; byte precip = 0;
            for (int i = 0; i < n; i++)
            {
                float w = c[i].Weight;
                if (w <= 0f) continue;
                var x = c[i].Weather;
                mean += w * x.GroundTemperatureC;
                lapse += w * (x.LapseRateKPerKm > 0f ? x.LapseRateKPerKm : 6.5f);
                slp += w * (x.SeaLevelPressureHpa > 0f ? x.SeaLevelPressureHpa : 1013.25f);
                rh += w * x.RelativeHumidity;
                wind += w * WindOf(x, point.Z, ground, out float g);
                gust += w * g;
                sigma += w * 3.912f / VisibilityOf(x, point.Z - ground);
                octas += w * TotalOctas(x);
                octasAbove += w * OctasAbove(x, point.Z);
                precipRate += w * x.PrecipitationMmPerHour;
                if (w > strongest) { strongest = w; ceiling = CeilingOf(x); precip = x.Precipitation; deck = CloudDeck.Of(x); }
            }

            var s = new WeatherSample
            {
                Utc = utc, RelativeHumidity = rh, Wind = wind, WindGustMps = gust, ExtinctionPerM = sigma,
                VisibilityM = sigma > 0f ? 3.912f / sigma : float.PositiveInfinity, CloudOctas = octas, CloudOctasAbove = octasAbove, CeilingM = ceiling, Clouds = deck,
                Precipitation = (PrecipitationKind)precip, PrecipitationMmPerHour = precipRate,
                SunElevationDeg = sun.ElevationDeg, SunAzimuthDeg = sun.AzimuthDeg,
            };

            // irradiance: 1361 · sin(elevation) · τ × the Kasten–Czeplak cloud cut 1 − 0.75 (N/8)^3.4 — N the cover ABOVE the point (§6.3)
            float cloudCut = 1f - 0.75f * MathF.Pow(Math.Clamp(octasAbove / 8f, 0f, 1f), 3.4f);
            float sinEl = (float)Math.Sin(sun.ElevationDeg * Math.PI / 180.0);
            s.IrradianceWm2 = sinEl > 0f ? SolarConstant * sinEl * ClearSkyTransmittance * cloudCut : 0f;
            (s.Light, s.RelativeLight) = LightOf(sun.ElevationDeg, cloudCut);

            // temperature: the daily mean + a closed-form diurnal swing (±6 K clear … ±2 K overcast, the peak ~2.5 h after solar noon)
            float amplitude = 2f + 4f * (1f - Math.Clamp(octas / 8f, 0f, 1f));
            s.DiurnalPhase = (float)Math.Cos(2 * Math.PI * (sun.SolarHour - 14.5) / 24.0);
            s.GroundTemperatureC = mean + amplitude * s.DiurnalPhase;
            float hAgl = MathF.Max(0f, point.Z - ground);
            s.TemperatureC = s.GroundTemperatureC - lapse * hAgl / 1000f;

            // pressure: the barometric formula from the sea-level pressure with the local temperature profile (ISA, §4)
            float lapseK = lapse / 1000f;
            float tSeaK = s.GroundTemperatureC + 273.15f + lapseK * ground;   // the ground temperature carried down to sea level
            float tHereK = s.TemperatureC + 273.15f;
            float exponent = Gravity / (GasConstantDryAir * MathF.Max(lapseK, 1e-6f));
            s.PressureHpa = slp * MathF.Pow(MathF.Max(tHereK, 1f) / MathF.Max(tSeaK, 1f), exponent);
            s.DensityKgM3 = s.PressureHpa * 100f / (GasConstantDryAir * MathF.Max(tHereK, 1f));
            return s;
        }

        /// <summary>The wind of one weather at altitude <paramref name="altitudeM"/> (MSL) over ground at <paramref name="groundZ"/> (W-3):
        /// the levels interpolated as VECTORS; below the lowest a log profile to zero at the ground; above the highest the highest.</summary>
        public static unsafe Vector2 WindOf(WeatherConditions w, float altitudeM, float groundZ, out float gustMps)
        {
            gustMps = 0f;
            int n = Math.Min((int)w.WindCount, WeatherConditions.MaxWindLevels);
            if (n == 0) return Vector2.Zero;
            if (altitudeM <= w.WindAltitudeM[0])
            {
                float a0 = w.WindAltitudeM[0];
                float h0 = MathF.Max(a0 - groundZ, RoughnessM * 2.718f);
                float h = MathF.Max(altitudeM - groundZ, RoughnessM);
                float f = a0 <= groundZ ? 1f : Math.Clamp(MathF.Log(h / RoughnessM) / MathF.Log(h0 / RoughnessM), 0f, 1f);
                gustMps = w.WindGustMps[0] * f;
                return Toward(w.WindFromDeg[0], w.WindSpeedMps[0]) * f;
            }
            for (int i = 1; i < n; i++)
                if (altitudeM <= w.WindAltitudeM[i])
                {
                    float k = (altitudeM - w.WindAltitudeM[i - 1]) / MathF.Max(w.WindAltitudeM[i] - w.WindAltitudeM[i - 1], 1e-3f);
                    gustMps = w.WindGustMps[i - 1] + k * (w.WindGustMps[i] - w.WindGustMps[i - 1]);
                    return Vector2.Lerp(Toward(w.WindFromDeg[i - 1], w.WindSpeedMps[i - 1]), Toward(w.WindFromDeg[i], w.WindSpeedMps[i]), k);
                }
            gustMps = w.WindGustMps[n - 1];
            return Toward(w.WindFromDeg[n - 1], w.WindSpeedMps[n - 1]);
        }

        /// <summary>The velocity of a wind blowing FROM <paramref name="fromDeg"/> (compass) at <paramref name="speed"/>: X east, Y north.</summary>
        public static Vector2 Toward(float fromDeg, float speed)
        {
            float r = fromDeg * MathF.PI / 180f;
            return new Vector2(-MathF.Sin(r), -MathF.Cos(r)) * speed;
        }

        /// <summary>Visibility of one weather at <paramref name="heightAboveGround"/>: inside the fog layer the fog's, else the clear air's.</summary>
        public static float VisibilityOf(in WeatherConditions w, float heightAboveGround)
        {
            float v = w.FogTopM > 0f && heightAboveGround < w.FogTopM ? w.FogVisibilityM : w.VisibilityM;
            return MathF.Max(v, 1f);
        }

        /// <summary>Total sky cover of one weather, octas — layers overlapping at random: <c>8 · (1 − Π(1 − Nᵢ/8))</c>.</summary>
        public static unsafe float TotalOctas(WeatherConditions w)
        {
            float clear = 1f;
            int n = Math.Min((int)w.CloudCount, WeatherConditions.MaxCloudLayers);
            for (int i = 0; i < n; i++) clear *= 1f - Math.Clamp(w.CloudOctas[i] / 8f, 0f, 1f);
            return 8f * (1f - clear);
        }

        /// <summary>⭐ §6.3 — the cover over <paramref name="altitudeM"/> (MSL): the layers whose top is above it (a point inside a cloud is
        /// shaded by it), overlapping at random like <see cref="TotalOctas"/>.</summary>
        public static unsafe float OctasAbove(WeatherConditions w, float altitudeM)
        {
            float clear = 1f;
            int n = Math.Min((int)w.CloudCount, WeatherConditions.MaxCloudLayers);
            for (int i = 0; i < n; i++)
                if (w.CloudBaseM[i] + MathF.Max(w.CloudThicknessM[i], 0f) > altitudeM) clear *= 1f - Math.Clamp(w.CloudOctas[i] / 8f, 0f, 1f);
            return 8f * (1f - clear);
        }

        /// <summary>The lowest base (m MSL) of a layer of ≥ 5 octas (broken or overcast); +∞ when none.</summary>
        public static unsafe float CeilingOf(WeatherConditions w)
        {
            float c = float.PositiveInfinity;
            int n = Math.Min((int)w.CloudCount, WeatherConditions.MaxCloudLayers);
            for (int i = 0; i < n; i++) if (w.CloudOctas[i] >= 5 && w.CloudBaseM[i] < c) c = w.CloudBaseM[i];
            return c;
        }

        /// <summary>The light band (day · civil · nautical · astronomical twilight · night at 0°/−6°/−12°/−18°) and the light relative to a
        /// clear noon, dimmed by cloud.</summary>
        public static (LightBand Band, float Relative) LightOf(double elevationDeg, float cloudCut)
        {
            if (elevationDeg > 0)
                return (LightBand.Day, Math.Clamp(0.3f + 0.7f * MathF.Min(1f, (float)Math.Sin(elevationDeg * Math.PI / 180.0) / 0.5f), 0f, 1f) * cloudCut);
            if (elevationDeg > -6)  return (LightBand.CivilTwilight,        Lerp(0.05f, 0.3f, (float)(elevationDeg + 6) / 6f) * cloudCut);
            if (elevationDeg > -12) return (LightBand.NauticalTwilight,     Lerp(0.005f, 0.05f, (float)(elevationDeg + 12) / 6f) * cloudCut);
            if (elevationDeg > -18) return (LightBand.AstronomicalTwilight, Lerp(0.001f, 0.005f, (float)(elevationDeg + 18) / 6f) * cloudCut);
            return (LightBand.Night, 0.001f * cloudCut);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

        private static float GroundZ(EntityRepository world, Vector3 point)
            => Fdp.Toolkit.World.WorldQuery.Of(world) is { } q ? q.GroundHeightAt(point.X, point.Y) : 0f;

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>An outline's relative points seen in world XY (no copy).</summary>
        private readonly struct OffsetPolygon : IReadOnlyList<Vector2>
        {
            private readonly IAreaOutline _outline;
            private readonly Vector2 _origin;
            public OffsetPolygon(IAreaOutline outline, Vector2 origin) { _outline = outline; _origin = origin; }
            public Vector2 this[int index] => _outline.Points[index] + _origin;
            public int Count => _outline.Points.Count;
            public IEnumerator<Vector2> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

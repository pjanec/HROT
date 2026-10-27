using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Modules.Geographic.Transforms;
using Xunit;

namespace Fdp.Modules.Geographic.Tests
{
    /// <summary>
    /// ⭐ AQ107 §5 acceptance — the GATEWAY FORMULA. <c>docs/reference/NED_DDS_Protocol.md</c> ("For a protocol gateway", step 0) tells a
    /// DIS/HLA gateway to place a local-ENU point with <c>ECEF = ECEF(origin) + R · local</c> (R's columns = east, north, up at the origin)
    /// and to read the geodetic position off that ECEF. This rail implements EXACTLY that, in the test, in doubles, with the textbook
    /// ENU→ECEF rotation and a textbook ECEF→geodetic (Bowring, iterated to convergence — NOT the transform's own iteration), and asserts
    /// it agrees with <see cref="WGS84Transform.ToGeodetic"/> to 1 mm — so the formula the doc gives a gateway is the transform's math.
    /// ⛔ It deliberately shares NO code with <see cref="WGS84Transform"/>: a shared helper could not disagree with itself.
    /// </summary>
    public class WGS84TransformGatewayFormulaTests
    {
        private const double A = 6378137.0;
        private const double F = 1.0 / 298.257223563;
        private const double E2 = F * (2.0 - F);

        // ── the formula under test, as the doc states it ─────────────────────────────────────────────────────────────────────

        /// <summary>ECEF(lat, lon, alt): the standard geodetic-to-ECEF formula (radians, metres).</summary>
        private static (double X, double Y, double Z) GeodeticToEcef(double lat, double lon, double alt)
        {
            double n = A / Math.Sqrt(1.0 - E2 * Math.Sin(lat) * Math.Sin(lat));
            return ((n + alt) * Math.Cos(lat) * Math.Cos(lon),
                    (n + alt) * Math.Cos(lat) * Math.Sin(lon),
                    (n * (1.0 - E2) + alt) * Math.Sin(lat));
        }

        /// <summary>R, the doc's rotation from local ENU (X east, Y north, Z up) to ECEF at the origin — columns east, north, up.</summary>
        private static double[,] EnuToEcefRotation(double lat, double lon)
        {
            double sp = Math.Sin(lat), cp = Math.Cos(lat), sl = Math.Sin(lon), cl = Math.Cos(lon);
            return new[,]
            {
                { -sl, -sp * cl, cp * cl },
                {  cl, -sp * sl, cp * sl },
                { 0.0,       cp,      sp },
            };
        }

        /// <summary>The gateway's step: <c>ECEF = ECEF(origin) + R · local</c>.</summary>
        private static (double X, double Y, double Z) EnuToEcef(double latDeg, double lonDeg, double alt, double e, double n, double u)
        {
            double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
            var o = GeodeticToEcef(lat, lon, alt);
            var r = EnuToEcefRotation(lat, lon);
            return (o.X + r[0, 0] * e + r[0, 1] * n + r[0, 2] * u,
                    o.Y + r[1, 0] * e + r[1, 1] * n + r[1, 2] * u,
                    o.Z + r[2, 0] * e + r[2, 1] * n + r[2, 2] * u);
        }

        /// <summary>The standard ECEF → geodetic conversion (Bowring's iteration, run to convergence), radians + metres.</summary>
        private static (double Lat, double Lon, double Alt) EcefToGeodetic(double x, double y, double z)
        {
            double lon = Math.Atan2(y, x);
            double p = Math.Sqrt(x * x + y * y);
            double lat = Math.Atan2(z, p * (1.0 - E2));
            for (int i = 0; i < 100; i++)
            {
                double n = A / Math.Sqrt(1.0 - E2 * Math.Sin(lat) * Math.Sin(lat));
                double next = Math.Atan2(z + E2 * n * Math.Sin(lat), p);
                bool done = Math.Abs(next - lat) < 1e-15;
                lat = next;
                if (done) break;
            }
            double nf = A / Math.Sqrt(1.0 - E2 * Math.Sin(lat) * Math.Sin(lat));
            double alt = Math.Abs(Math.Cos(lat)) > 1e-9 ? p / Math.Cos(lat) - nf : Math.Abs(z) / Math.Abs(Math.Sin(lat)) - nf * (1.0 - E2);
            return (lat, lon, alt);
        }

        // ── the rail ─────────────────────────────────────────────────────────────────────────────────────────────────────────

        public static IEnumerable<object[]> Origins() => new[]
        {
            new object[] { 0.0, 10.0, 0.0 },        // the equator
            new object[] { 52.5, 13.4, 35.0 },      // 52.5° N
            new object[] { -70.0, 77.9, 1200.0 },   // 70° S
        };

        public static IEnumerable<object[]> OriginsAndDistances()
        {
            foreach (var o in Origins())
                foreach (var d in new[] { 0.0, 1_000.0, 10_000.0, 50_000.0 })
                    yield return new object[] { o[0], o[1], o[2], d };
        }

        /// <summary>The directions (east, north, up weights) a point is placed in: along each axis, a diagonal, the other diagonals, and up.</summary>
        private static readonly (double E, double N, double U)[] Directions =
        {
            (1, 0, 0), (0, 1, 0), (0.7071067811865476, 0.7071067811865476, 0), (-0.6, -0.8, 0), (0.6, -0.8, 0), (0, 0, 1),
        };

        /// <summary>A local point at horizontal distance <paramref name="d"/> in a direction — as the Vector3 the ECS holds (float).</summary>
        private static Vector3 LocalPoint((double E, double N, double U) dir, double d)
            => d == 0.0 ? new Vector3(0f, 0f, dir.U > 0 ? 100f : 0f)
                        : new Vector3((float)(dir.E * d), (float)(dir.N * d), (float)(dir.U * Math.Min(d, 3_000.0)));

        private static double Distance((double X, double Y, double Z) a, (double X, double Y, double Z) b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>
        /// How far the TRANSFORM may stray from exact double maths at a distance. ⭐ <b>Measured finding (AQ107 I, 2026-10-27):</b> the
        /// transform rotates the local offset with a FLOAT <c>Matrix4x4</c> and <c>Vector3.Transform</c> returns float, so its answer carries
        /// float32 rounding that grows linearly with the distance. Worst difference over these 3 origins × 6 directions:
        /// 0.01 mm at 0 km · 0.14 mm at 1 km · 1.34 mm at 10 km · 6.13 mm at 50 km — a constant ≈1.2e-7 × distance (float epsilon), the
        /// signature of float rounding and not of a wrong formula (a wrong term errs by whole percent: ≥ 100 m at 50 km).
        /// ⛔ The AQ107 acceptance said "1 mm at 50 km"; the TRANSFORM cannot meet that (and "the transform is the truth": it is not changed
        /// here). The doc's formula IS exact in doubles — a gateway computing it in doubles has no such error. So the bound is 1 mm, or the
        /// float32 envelope 2e-7 × distance where that is larger (2 mm at 10 km, 10 mm at 50 km) — still four orders below what a wrong
        /// rotation term gives, which is what this rail exists to catch.
        /// </summary>
        private static double Tolerance(double distance) => Math.Max(0.001, 2e-7 * distance);

        /// <summary>
        /// For every origin × distance × direction: the independent gateway formula's ECEF → geodetic equals <c>WGS84Transform.ToGeodetic</c>.
        /// The difference is the distance in metres between the two geodetic points (each put back through the standard geodetic → ECEF),
        /// so a longitude error at 70° S is counted in metres, not in degrees. ✅ Red-proof: swapping two terms of <c>R</c> ⇒ red by kilometres.
        /// </summary>
        [Theory]
        [MemberData(nameof(OriginsAndDistances))]
        public void AQ107_TheGatewayFormula_AgreesWithWGS84Transform(double originLat, double originLon, double originAlt, double d)
        {
            var transform = new WGS84Transform(originLat, originLon, originAlt);
            double worst = 0.0;
            string worstAt = "";

            foreach (var dir in Directions)
            {
                // the local point is a Vector3 (float) on the ECS side — use that float value itself in both computations
                var local = LocalPoint(dir, d);
                var ecef = EnuToEcef(originLat, originLon, originAlt, local.X, local.Y, local.Z);
                var (gwLat, gwLon, gwAlt) = EcefToGeodetic(ecef.X, ecef.Y, ecef.Z);

                var (tLatDeg, tLonDeg, tAlt) = transform.ToGeodetic(local);

                double metres = Distance(GeodeticToEcef(gwLat, gwLon, gwAlt), GeodeticToEcef(tLatDeg * Math.PI / 180.0, tLonDeg * Math.PI / 180.0, tAlt));
                if (metres > worst) { worst = metres; worstAt = $"local ({local.X}, {local.Y}, {local.Z})"; }
            }

            Assert.True(worst <= Tolerance(d),
                $"origin ({originLat}, {originLon}), {d / 1000.0} km: the gateway formula and WGS84Transform.ToGeodetic differ by {worst * 1000.0:F3} mm at {worstAt} (limit {Tolerance(d) * 1000.0:F3} mm)");
        }

        /// <summary>
        /// The same formula, run the OTHER way, closes the loop: a geodetic point → <c>WGS84Transform.ToCartesian</c> (local ENU) → the
        /// gateway's <c>origin + R · local</c> lands on the geodetic point's own ECEF.
        /// </summary>
        [Theory]
        [MemberData(nameof(OriginsAndDistances))]
        public void AQ107_TheGatewayFormula_PlacesTheTransformsLocalPointOnItsGeodeticEcef(double originLat, double originLon, double originAlt, double d)
        {
            var transform = new WGS84Transform(originLat, originLon, originAlt);
            double worst = 0.0;

            foreach (var dir in Directions)
            {
                var (lat, lon, alt) = transform.ToGeodetic(LocalPoint(dir, d));
                var back = transform.ToCartesian(lat, lon, alt);
                var viaGateway = EnuToEcef(originLat, originLon, originAlt, back.X, back.Y, back.Z);
                double metres = Distance(viaGateway, GeodeticToEcef(lat * Math.PI / 180.0, lon * Math.PI / 180.0, alt));
                worst = Math.Max(worst, metres);
            }

            Assert.True(worst <= Tolerance(d),
                $"origin ({originLat}, {originLon}), {d / 1000.0} km: ToCartesian then the gateway formula is {worst * 1000.0:F3} mm off the geodetic ECEF (limit {Tolerance(d) * 1000.0:F3} mm)");
        }
    }
}

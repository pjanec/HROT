using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ Indirect fire IF-1 (I1; docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.3, IF-B, IF-C) — the gun's fire solution, beside
    /// <see cref="ThrowArcTests"/>: the LOWEST charge that reaches; low in the open, high behind a wall and into a walled courtyard; refused
    /// too far, too close (inside the elevation limit) and masked. A small range: a gun at (100, 100), a 20 m wall across y = 650 (x 50–150),
    /// a courtyard of 10 m walls round (400, 700). Charges 60 / 80 / 100 m/s (vacuum ranges ≈ 367 / 652 / 1019 m).
    /// </summary>
    public sealed class FireSolutionTests
    {
        private static readonly List<float> Charges = new() { 60f, 80f, 100f };
        private static readonly Vector3 Gun = new(100f, 100f, 1.5f);

        private static IWorldQuery Range()
        {
            var w = TerrainWorldParser.Parse("""
                { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 1200, 1200], "groundZ": 0 },
                  "features": [
                    { "type": "Feature", "properties": { "kind": "wall", "height": 20.0, "material": "concrete", "thickness": 1.0 },
                      "geometry": { "type": "LineString", "coordinates": [[50, 650], [150, 650]] } },
                    { "type": "Feature", "properties": { "kind": "wall", "height": 10.0, "material": "concrete", "thickness": 0.5 },
                      "geometry": { "type": "LineString", "coordinates": [[380, 680], [420, 680], [420, 720], [380, 720], [380, 680]] } }
                  ] }
                """, "if-range");
            return new TerrainWorldQuery(w, DoorStates.Authored(w));
        }

        private static WeaponMountDto Howitzer => new() { Arc = IndirectArc.Both, MinElevationDeg = -5f, MaxElevationDeg = 85f };
        private static WeaponMountDto Mortar => new() { Arc = IndirectArc.HighOnly, MinElevationDeg = 45f, MaxElevationDeg = 85f };

        [Fact]
        public void IF1_TheLowestChargeThatReaches_IsChosen_LowInTheOpen()
        {
            var q = Range();
            var near = FireSolution.For(q, Gun, new Vector3(100f, 600f, 0f), Howitzer, Charges);   // 500 m: charge 2 (652 m) is the first
            Assert.True(near.Ok);
            Assert.Equal((byte)2, near.Charge);
            Assert.False(near.High);
            var far = FireSolution.For(q, Gun, new Vector3(700f, 800f, 0f), Howitzer, Charges);    // ≈ 922 m: only charge 3
            Assert.Equal(((byte)3, false), (far.Charge, far.High));
            Assert.True(far.TimeOfFlight > 9f && far.Elevation < 45f, $"a low arc: {far.TimeOfFlight} s at {far.Elevation}°");
        }

        [Fact]
        public void IF1_BehindAWall_AndIntoAWalledCourtyard_TheLowArcIsMasked_TheGunGoesHigh()
        {
            var q = Range();
            var behindWall = new Vector3(100f, 660f, 0f);
            var low = FireSolution.For(q, Gun, behindWall, Howitzer, Charges, ArcPreference.Low);
            Assert.Equal(FireRefusal.Masked, low.Refusal);                                        // premise: the wall stops the low arc
            var auto = FireSolution.For(q, Gun, behindWall, Howitzer, Charges);
            Assert.True(auto.Ok && auto.High, $"over the wall: {auto.Refusal}");
            Assert.True(auto.Elevation > 45f);

            var courtyard = new Vector3(400f, 700f, 0f);
            var yard = FireSolution.For(q, Gun, courtyard, Howitzer, Charges);
            Assert.True(yard.Ok && yard.High, $"into the courtyard: {yard.Refusal}");
        }

        [Fact]
        public void IF1_TooFar_TooClose_AndMasked_AreRefused_WithTheirReason()
        {
            var q = Range();
            Assert.Equal(FireRefusal.TooFar, FireSolution.For(q, Gun, new Vector3(100f, 1150f, 0f), Howitzer, Charges).Refusal);
            Assert.Equal(FireRefusal.TooClose, FireSolution.For(q, Gun, new Vector3(100f, 150f, 0f), Mortar, Charges).Refusal);   // > 85° needed
            var lowOnly = Howitzer with { Arc = IndirectArc.LowOnly };
            Assert.Equal(FireRefusal.Masked, FireSolution.For(q, Gun, new Vector3(100f, 660f, 0f), lowOnly, Charges).Refusal);

            // a mortar never fires low, even in the open
            var open = FireSolution.For(q, Gun, new Vector3(100f, 600f, 0f), Mortar, Charges);
            Assert.True(open.Ok && open.High && open.Elevation >= 45f);
        }

        [Fact]
        public void IF1_NoCharges_UsesTheMountsOneSpeed_AsChargeZero()
        {
            var mount = Howitzer with { MuzzleVelocity = 80f };
            var s = FireSolution.For(null, Gun, new Vector3(100f, 600f, 0f), mount, charges: null);
            Assert.True(s.Ok);
            Assert.Equal((byte)0, s.Charge);
            Assert.Equal(80f, s.Launch.Length(), 2);
        }
    }
}

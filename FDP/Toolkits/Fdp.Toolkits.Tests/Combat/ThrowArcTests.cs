using System;
using System.IO;
using System.Numerics;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ <c>WL-4</c> L-1 (📄 docs/DESIGN_Weapon_Layers.md §5f) — a throw whose low arc the terrain would stop is lobbed. Measured on
    /// <c>bt-range</c>'s 1.2 m sandbags (145–153, y 40): a crouched thrower 15 m south of a man crouched 2.5 m behind them.
    /// 🔴 Red before: every hand throw flew the low arc and burst against the wall's face.
    /// </summary>
    public sealed class ThrowArcTests
    {
        private const float M67Speed = 15f;

        private static IWorldQuery BtRange()
        {
            var folder = Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "bt-range");
            var w = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
            return new TerrainWorldQuery(w, DoorStates.Authored(w));
        }

        [Fact]
        public void WL4_L1_ALowThrowIntoTheSandbags_IsLobbed_AndTheLobClearsThem()
        {
            var q = BtRange();
            var from = new Vector3(149f, 27.5f, 1.1f);   // crouched, 15 m from the man
            var to   = new Vector3(149f, 42.5f, 0f);     // 2.5 m behind the wall
            var low  = FireProcessingSystem.ArcLaunch(from, to, M67Speed, high: false);
            Assert.False(ThrowArc.Clears(q, from, to, low, 0f), "premise: the low arc hits the 1.2 m sandbags");

            var v = ThrowArc.Launch(q, from, to, M67Speed, 0f);
            Assert.True(v.Z > low.Z + 1f, "the throw is lobbed (a steeper launch)");
            Assert.True(ThrowArc.Clears(q, from, to, v, 0f), "the lob passes over the sandbags");
        }

        [Fact]
        public void WL4_L1_AnOpenThrow_KeepsTheLowArc()
        {
            var q = BtRange();
            var from = new Vector3(175f, 27.5f, 1.1f);   // east of every wall on the range
            var to   = new Vector3(175f, 42.5f, 0f);
            var low  = FireProcessingSystem.ArcLaunch(from, to, M67Speed, high: false);
            Assert.True(ThrowArc.Clears(q, from, to, low, 0f));
            Assert.Equal(low, ThrowArc.Launch(q, from, to, M67Speed, 0f));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3296</c> — a SHORT throw over cover is TOSSED, not lobbed at full speed: 1.5 m in front of the 1.2 m sandbags, 2.5 m
        /// behind them, the low arc hits the bags; the throw clears them on the softest toss — its apex under 3 m and its flight inside a
        /// 1.5 s fuze — where the full-speed lob climbs ~11 m and flies ~3 s (behaviors' note: a stun grenade burst 12 m up).
        /// ✅ Red-proof: the toss search removed (the full-speed lob) ⇒ apex and flight red.
        /// </summary>
        [Fact]
        public void CE3296_AShortThrowOverCover_IsASoftToss_NotAFullSpeedLob()
        {
            var q = BtRange();
            var from = new Vector3(149f, 38.5f, 1.1f);   // crouched, 1.5 m south of the bags
            var to   = new Vector3(149f, 42.5f, 0f);     // 2.5 m behind them
            Assert.False(ThrowArc.Clears(q, from, to, FireProcessingSystem.ArcLaunch(from, to, M67Speed, high: false), 0f),
                "premise: the low arc hits the 1.2 m sandbags");

            Assert.True(ThrowArc.TryLaunch(q, from, to, M67Speed, 0f, out var v));
            Assert.True(ThrowArc.Clears(q, from, to, v, 0f));
            float apex = from.Z + v.Z * v.Z / (2f * CombatConstants.Gravity);
            float flight = 4f / new Vector2(v.X, v.Y).Length();
            Assert.True(apex < 3f, $"a toss, not a lob: apex {apex:F1} m");
            Assert.True(flight < 1.5f, $"inside a stun grenade's fuze: {flight:F2} s");
            Assert.True(v.Length() < M67Speed, $"softer than the full throw: {v.Length():F1} m/s");
        }

        /// <summary>
        /// ⭐ <c>CE-3296</c> — a throw nothing clears says so: from outside closed House A to a point inside it (walls, roof) every toss and
        /// the lob are stopped ⇒ false — the muscle refuses such a throw (<c>Masked</c>) instead of bursting it on the wall at the thrower's
        /// feet (behaviors' note: an M67 on the jamb, 143 to the thrower).
        /// </summary>
        [Fact]
        public void CE3296_AThrowNothingClears_IsReportedSo()
        {
            var q = BtRange();
            var from = new Vector3(105f, 98.5f, 1.6f);   // 1.5 m outside House A's south wall
            var to   = new Vector3(105f, 104f, 0.1f);    // inside, behind closed doors
            Assert.False(ThrowArc.TryLaunch(q, from, to, M67Speed, 0f, out _));
        }

        /// <summary>
        /// ⭐ <c>CE-3296</c> — a TIME-fused round never takes an arc that outlasts its fuze: whatever clears within a limit flies within it,
        /// and a limit nothing can meet (15 m in 0.8 s needs more than the throw's 15 m/s) reports the throw unclear — the muscle refuses
        /// it. 📌 Live, behaviors' check: a stun grenade lobbed at full speed burst 12 m up. ✅ Red-proof: the limit ignored ⇒ the 0.8 s
        /// case returns a clearing toss ⇒ red.
        /// </summary>
        [Fact]
        public void CE3296_ATimeFusedRound_NeverTakesAnArcThatOutlastsItsFuze()
        {
            var q = BtRange();
            var from = new Vector3(149f, 27.5f, 1.1f);
            var to   = new Vector3(149f, 42.5f, 0f);
            foreach (float limit in new[] { 1.2f, 1.45f, 2f, 3f })
                if (ThrowArc.TryLaunch(q, from, to, M67Speed, 0f, out var v, maxFlightSeconds: limit))
                    Assert.True(15f / new Vector2(v.X, v.Y).Length() <= limit + 1e-3f, $"within a {limit} s fuze");
            Assert.False(ThrowArc.TryLaunch(q, from, to, M67Speed, 0f, out _, maxFlightSeconds: 0.8f));
            Assert.True(ThrowArc.TryLaunch(q, from, to, M67Speed, 0f, out _));
        }

        [Fact]
        public void WL4_L1_ALauncherKeepsItsHighArc_AndNoTerrainKeepsTheLowThrow()
        {
            var from = new Vector3(0f, 0f, 1.7f); var to = new Vector3(20f, 0f, 0f);
            Assert.Equal(FireProcessingSystem.ArcLaunch(from, to, 60f, high: true), ThrowArc.Launch(null, from, to, 60f, 0f));
            Assert.Equal(FireProcessingSystem.ArcLaunch(from, to, M67Speed, high: false), ThrowArc.Launch(null, from, to, M67Speed, 0f));
        }
    }
}

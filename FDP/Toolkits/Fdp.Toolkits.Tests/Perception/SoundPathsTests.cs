using System;
using System.Numerics;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Perception
{
    /// <summary>
    /// ⭐⭐ AQ106 (R-3050; docs/blueprints/Architect_Question_106_Sound_Paths.md) — sound round corners and through doors and windows, on the
    /// shipped <c>house-2f</c> at the origin (10 × 8 m, an inner concrete wall at x 5 with the HALL door at (5, 6.6), the FRONT door at
    /// (4.2, 0) in the west room, an east window at (10, 3.6), the stair in the east room up to the one upper room), and K's street corner
    /// round a solid block. Each case states the straight line's loss and the path's, so a number change shows which side moved.
    /// </summary>
    public sealed class SoundPathsTests
    {
        private static readonly byte Steps = (byte)SoundSourceClass.Footsteps, Blast = (byte)SoundSourceClass.Explosion;

        private static TerrainWorld House(string doors)
        {
            var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "bt-range");
            string json = "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-50,-50,50,50],\"groundZ\":0},\"features\":[" +
                          "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"template\":\"house-2f\",\"label\":\"H\",\"baseZ\":0,\"rotation\":0," +
                          "\"doors\":{" + doors + "}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0]}}]}";
            return TerrainWorldParser.Parse(json, "h", TerrainAssets.ForFolder(folder));
        }

        private static SoundRoute Route(TerrainWorld w, Vector3 ear, Vector3 source, byte cls)
            => SoundPaths.Route(new TerrainWorldQuery(w), w, DoorStates.Authored(w), ear, source, cls);

        /// <summary>⭐ C / D / F — steps in the west room, an ear in the east room: the straight line crosses the inner wall (a halving); with the
        /// hall door OPEN the sound comes round through it — louder, and heard FROM the doorway; with it CLOSED the door costs its halving
        /// and the straight line wins again. ✅ Red-proof: <c>ThroughOpenings</c> returning null ⇒ always Straight ⇒ red.</summary>
        [Fact]
        public void AQ106_RoundTheCorner_ThroughTheHallDoor_HeardFromTheDoorway_AClosedDoorMuffles()
        {
            var source = new Vector3(2f, 6.6f, 0f); var ear = new Vector3(8f, 4f, 0f);
            var open = Route(House("\"hall\":\"open\""), ear, source, Steps);
            Assert.Equal(SoundVia.Openings, open.Via);
            Assert.True(open.Factor > 0.8f, $"through the open door {open}");
            Assert.True(Vector2.Distance(new Vector2(open.HeardFrom.X, open.HeardFrom.Y), new Vector2(5f, 6.6f)) < 0.3f, $"heard from {open.HeardFrom}");
            Assert.Equal(0.501f, Db(open.StraightDb), 2);   // the straight line: one halving (the inner wall)

            var shut = Route(House("\"hall\":\"closed\""), ear, source, Steps);
            Assert.Equal(SoundVia.Straight, shut.Via);
            Assert.True(shut.Factor < open.Factor);
            Assert.Equal(source, shut.HeardFrom);
        }

        /// <summary>⭐ E — a WINDOW is an opening: steps in the east room, an ear in the street south of the house; the straight line crosses
        /// the south wall, the way out through the east window is louder and heard from the window.</summary>
        [Fact]
        public void AQ106_AWindowIsAnOpening()
        {
            var r = Route(House(""), new Vector3(4.2f, -6f, 0f), new Vector3(8f, 4f, 0f), Steps);
            Assert.Equal(SoundVia.Openings, r.Via);
            Assert.True(r.Factor > Db(r.StraightDb), r.ToString());
            Assert.True(r.HeardFrom.X > 9.5f || r.HeardFrom.Y < 0.5f, $"heard from an opening, not through the wall: {r.HeardFrom}");
        }

        /// <summary>⭐ J — upstairs via the stair: steps in the upper room, an ear in the east room below; the straight line crosses the slab,
        /// the stairwell is the louder way.</summary>
        [Fact]
        public void AQ106_StepsUpstairs_ComeDownTheStair()
        {
            var r = Route(House(""), new Vector3(9f, 2f, 0f), new Vector3(2f, 4f, 3f), Steps);
            Assert.Equal(SoundVia.Openings, r.Via);
            Assert.True(r.Factor > Db(r.StraightDb), r.ToString());
        }

        /// <summary>⭐ B / C — a low sound loses little at a corner and little through a wall, a high one more; nothing between ⇒ the straight
        /// line at once (no search).</summary>
        [Fact]
        public void AQ106_TheBandScalesTheLoss_AndOpenAirIsStraight()
        {
            var w = House("\"hall\":\"closed\"");
            var source = new Vector3(2f, 6.6f, 0f); var ear = new Vector3(8f, 4f, 0f);
            Assert.True(Route(w, ear, source, Blast).Factor > Route(w, ear, source, Steps).Factor);
            var open = Route(w, new Vector3(-20f, -20f, 0f), new Vector3(-10f, -20f, 0f), Steps);
            Assert.Equal((SoundVia.Straight, 1f, 0), (open.Via, open.Factor, open.Crossings));
        }

        /// <summary>
        /// ⭐ K (🔒 user, 2026-10-25: <i>"'behind a corner' on the street should count as some attenuator too"</i>) — two streets either side of
        /// a solid 20 × 20 m block: through it is two faces (two halvings), round it is two corners — round wins, heard from the far corner.
        /// A man round ONE corner hears less than in the open street. ✅ Red-proof: <c>AroundBuildings</c> returning null ⇒ Straight ⇒ red.
        /// </summary>
        [Fact]
        public void AQ106K_RoundAStreetCorner_IsAnAttenuator_AndBeatsGoingThroughTheBlock()
        {
            const string block = "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,100,100],\"groundZ\":0},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"B\",\"building\":{\"solid\":true,\"footprint\":[[0,0],[20,0],[20,20],[0,20]],\"height\":10}}," +
                "\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0]}}]}";
            var w = TerrainWorldParser.Parse(block, "k");
            var across = Route(w, new Vector3(25f, 10f, 0f), new Vector3(-5f, 10f, 0f), Steps);
            Assert.True(across.Crossings >= 2, $"through the block is two faces at least: {across}");
            Assert.Equal(SoundVia.AroundBuildings, across.Via);
            Assert.True(across.Factor > Db(across.StraightDb), across.ToString());
            Assert.Equal(2, across.Corners);
            Assert.True(MathF.Abs(across.HeardFrom.X - 20f) < 0.5f, $"heard from the far corner: {across.HeardFrom}");

            var corner = Route(w, new Vector3(23f, 15f, 0f), new Vector3(15f, -3f, 0f), Steps);   // the south street round to the east one
            Assert.Equal(SoundVia.AroundBuildings, corner.Via);
            Assert.Equal(1, corner.Corners);   // a ~48° turn at (20, 0)
            Assert.InRange(corner.Factor, 0.3f, 0.9f);   // an attenuator: quieter than the open street, louder than through the block
        }

        private static float Db(float db) => MathF.Pow(10f, -db / 20f);
    }
}

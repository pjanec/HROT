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
            // ⭐ CE-2479 + CE-2475: the concrete inner wall costs the steps its real 40 dB, so the open door's way wins by far more than 20 dB ⇒ the first
            //   wavefront through the wall is lost: heard FROM THE DOORWAY (as AQ106 F)
            Assert.Equal(SoundVia.Openings, open.Via);
            Assert.True(open.Factor > 0.8f, $"through the open door {open}");
            Assert.True(Vector2.Distance(new Vector2(open.HeardFrom.X, open.HeardFrom.Y), new Vector2(5f, 6.6f)) < 0.3f, $"heard from {open.HeardFrom}");
            Assert.Equal(40f, open.StraightDb, 1);   // ⭐ CE-2479 — the straight line: the concrete inner wall's REAL 40 dB (was one halving)

            // ⭐ CE-2479 — the shut WOODEN door (15 dB) is still far quieter a way than the concrete wall (40): muffled, and through the door
            var shut = Route(House("\"hall\":\"closed\""), ear, source, Steps);
            Assert.Equal(SoundVia.Openings, shut.Via);
            Assert.True(shut.Factor < open.Factor * 0.5f, $"a shut door muffles: {shut.Factor} vs {open.Factor}");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2475</c> (R-2142; Sensing §8.6e; 🔒 user: "the human aggregates those and is not absolutely without the sense of from what direction the
        /// sound comes from") — the FIRST WAVEFRONT: the way round is louder, but the straight way arrives first. Up to 10 dB louder round ⇒ heard from
        /// the SOURCE's direction (straight); 20 dB or more ⇒ from the opening (as AQ106 F); between ⇒ smeared between the two bearings, less sure, and
        /// "through the opening" only past halfway. The way round still decides WHETHER it is heard (its factor is kept — AQ106 B).
        /// 🔴 Red-proof: <c>FirstWavefront</c> returning the way round unchanged ⇒ the 4 dB case is heard from the door ⇒ red.
        /// </summary>
        [Fact]
        public void CE2475_TheFirstWavefront_SetsTheDirection_UnlessTheWayRoundIsMuchLouder()
        {
            var ear = new Vector3(0f, 0f, 0f); var source = new Vector3(10f, 0f, 0f); var door = new Vector3(0f, 10f, 0f);
            var straight = new SoundRoute(0.5f, source, SoundVia.Straight, 6f, 10f, 6f, 0, 0, 1);
            SoundRoute Round(float leadDb) => new(0.5f * MathF.Pow(10f, leadDb / 20f), door, SoundVia.Openings, 6f, 14f, 1f, 1, 1, 1);

            var close = SoundPaths.FirstWavefront(Round(4f), straight, ear, source);
            Assert.Equal(SoundVia.Straight, close.Via);
            Assert.Equal(source, close.HeardFrom);
            Assert.Equal(Round(4f).Factor, close.Factor, 5);   // still heard as loud as the way round
            Assert.Equal(0f, close.Smear);

            var far = SoundPaths.FirstWavefront(Round(26f), straight, ear, source);
            Assert.Equal((SoundVia.Openings, door), (far.Via, far.HeardFrom));

            var smearedEarly = SoundPaths.FirstWavefront(Round(12f), straight, ear, source);
            var smearedLate = SoundPaths.FirstWavefront(Round(18f), straight, ear, source);
            Assert.Equal(SoundVia.Straight, smearedEarly.Via);
            Assert.Equal(SoundVia.Openings, smearedLate.Via);
            Assert.InRange(smearedEarly.Smear, 0.15f, 0.25f);
            float bearingEarly = MathF.Atan2(smearedEarly.HeardFrom.Y, smearedEarly.HeardFrom.X), bearingLate = MathF.Atan2(smearedLate.HeardFrom.Y, smearedLate.HeardFrom.X);
            Assert.True(bearingEarly > 0.05f && bearingEarly < bearingLate && bearingLate < MathF.PI / 2 - 0.05f, $"between the two bearings: {bearingEarly} < {bearingLate}");
            // ⭐ CE-2492 (📐 pe-wall-and-door) — the pull is carried so the answer's circle reaches back to the source: the man stands inside it
            Assert.Equal(Vector3.Distance(smearedEarly.HeardFrom, source), smearedEarly.SmearMetres, 4);
            Assert.True(smearedEarly.SmearMetres > 1f, $"pulled {smearedEarly.SmearMetres} m");
            Assert.Equal(0f, close.SmearMetres);
        }

        /// <summary>⭐ <c>CE-2475</c> + <c>CE-2479</c> on the real house (house-2f: brick outside, a CONCRETE inner wall, 40 dB) — a shot in the west room, an ear in
        /// the east room. Hall door OPEN: the doorway is ~20 dB louder than the wall ⇒ heard FROM THE DOORWAY (the way round dominates). Hall door SHUT (a wooden
        /// door, 15 dB): the door way leads by ~10–20 dB ⇒ the image SMEARS between the shot's direction and the door's. A BLAST (Low band, the wall costs it 8 dB)
        /// ⇒ heard from its own direction through the wall — the first wavefront. 📐 measured `2026-11-22` (TmpRouteDump).</summary>
        [Fact]
        public void CE2475_InARealHouse_TheDoorWins_AShutDoorSmears_ABlastComesThroughTheWall()
        {
            var source = new Vector3(2f, 6.6f, 0f); var ear = new Vector3(8f, 4f, 0f);
            var open = Route(House("\"hall\":\"open\""), ear, source, (byte)SoundSourceClass.SmallArms);
            Assert.Equal(SoundVia.Openings, open.Via);
            Assert.True(Vector2.Distance(new Vector2(open.HeardFrom.X, open.HeardFrom.Y), new Vector2(5f, 6.6f)) < 0.3f, $"from the doorway: {open.HeardFrom}");
            var shut = Route(House("\"hall\":\"closed\""), ear, source, (byte)SoundSourceClass.SmallArms);
            Assert.InRange(shut.Smear, 0.05f, 0.95f);
            var blast = Route(House("\"hall\":\"closed\""), ear, source, Blast);
            Assert.Equal(SoundVia.Straight, blast.Via);
            Assert.Equal(source, blast.HeardFrom);
        }

        /// <summary>⭐ <c>CE-2483</c> (📐 ia-house-hunt) — a man STANDING IN the opening a sound came through does not hear it "at his own feet": steps upstairs, the
        /// listener on the stair's foot portal ⇒ heard from beyond it (the source, or the opening before), never within 1.5 m of himself. From the east room's far
        /// corner the same steps still come from the stairwell (AQ106 F unchanged). 🔴 Red-proof: the skip loop removed ⇒ heard from the opening under him ⇒ red.</summary>
        [Fact]
        public void CE2483_StandingInTheOpening_TheSoundComesFromBeyondIt()
        {
            var w = House("");
            var source = new Vector3(2f, 4f, 3f);
            var far = Route(w, new Vector3(9f, 2f, 0f), source, Steps);
            Assert.Equal(SoundVia.Openings, far.Via);
            var stairFoot = far.HeardFrom;   // the stairwell, as heard from across the room
            var onIt = Route(w, new Vector3(stairFoot.X, stairFoot.Y, 0.5f), source, Steps);
            Assert.True(Vector3.Distance(onIt.HeardFrom, new Vector3(stairFoot.X, stairFoot.Y, 0.5f)) >= SoundPaths.StandingInOpeningM,
                        $"not at his own feet: heard from {onIt.HeardFrom}, he stands at {stairFoot}");
        }

        /// <summary>⭐ E — a WINDOW is an opening: steps in the east room, an ear in the street south of the house; the straight line crosses
        /// the south wall, the way out through the east window is louder and heard from the window.</summary>
        [Fact]
        public void AQ106_AWindowIsAnOpening()
        {
            var r = Route(House(""), new Vector3(4.2f, -6f, 0f), new Vector3(8f, 4f, 0f), Steps);
            Assert.Equal(SoundVia.Openings, r.Via);
            Assert.True(r.Factor > Db(r.StraightDb), r.ToString());
            // ⭐ CE-2479 — with the walls' real loss the way out may be the back door rather than the window: an opening on the house's edge either way
            Assert.True(r.HeardFrom.X > 9.5f || r.HeardFrom.Y < 0.5f || r.HeardFrom.Y > 7.5f || r.HeardFrom.X < 0.5f, $"heard from an opening, not through the wall: {r.HeardFrom}");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-5033</c> (glass windows D-5) — a glazed window is a portal CLOSED while its pane is intact (glass's 20 dB) and OPEN once broken: the same steps,
        /// the same street ear, louder once the panes are broken — 🔒 "any broken window would let sound into a building", by construction. 🔴 Red-proof: ignore
        /// <c>PaneBroken</c> in <c>ThroughOpenings</c> ⇒ the two routes are equal.
        /// </summary>
        [Fact]
        public void CE5033_AnIntactPane_Muffles_ABrokenOneLetsTheSoundOut()
        {
            var w = House("");
            Assert.NotEmpty(w.PaneLeaves);
            var ear = new Vector3(4.2f, -6f, 0f); var source = new Vector3(8f, 4f, 0f);
            var intact = Route(w, ear, source, Steps);

            using var repo = new Fdp.Core.EntityRepository();
            repo.RegisterComponent<WindowDamage>();
            var d = new WindowDamage { Building = 0 };
            foreach (var (panel, opening) in w.Panes) WindowDamages.Break(ref d, panel, opening);
            repo.AddComponent(repo.CreateEntity(), d);
            var windows = DoorStates.Of(repo, w);
            Assert.NotNull(windows.Windows);
            var broken = SoundPaths.Route(new TerrainWorldQuery(w), w, windows, ear, source, Steps);

            Assert.True(broken.Factor > intact.Factor * 1.5f, $"intact {intact} · broken {broken}");
            Assert.Equal(SoundVia.Openings, broken.Via);
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

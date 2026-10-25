using System.Linq;
using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Interfaces;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Perception.Tests;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Perception
{
    /// <summary>
    /// ⭐⭐ AQ104 E ② / AQ105 D (R-2079, R-2080; docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.8) — what the hearing sensor hears
    /// through a building, and a shout. A wall or a slab between halves how far a high sound carries (never more than its material
    /// stops), a low one loses a fifth of that; a man whose Utterance is a shouted kind is heard ~40 m while he speaks, talk is not.
    /// </summary>
    public sealed class AcousticHearingTests
    {
        // a two-storey house, 10 x 10 m at the origin, a slab at z 3; no interior walls, no openings
        private const string Walls = "\"walls\":[{\"from\":[0,0],\"to\":[10,0]},{\"from\":[10,0],\"to\":[10,10]},{\"from\":[10,10],\"to\":[0,10]},{\"from\":[0,10],\"to\":[0,0]}]";
        private static readonly string House =
            "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,200,100],\"groundZ\":0},\"features\":[" +
            "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"H\",\"building\":{\"footprint\":[[0,0],[10,0],[10,10],[0,10]]," +
            "\"storeys\":[{\"height\":3," + Walls + "},{\"height\":3," + Walls + "}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0]}}]}";

        private static EntityRepository World(bool house)
        {
            var w = PerceptionTestWorldFactory.Create();
            w.RegisterComponent<AcousticEmitter>();
            w.RegisterComponent<Utterance>();
            w.RegisterComponent<EqsSensor>();
            w.RegisterManagedComponent<SensorCapability>();
            w.SetSingleton(new GlobalTime { TotalTime = 0 });
            if (house)
            {
                w.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
                w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(House, "h"));
            }
            return w;
        }

        private static Entity Man(EntityRepository w, Vector3 at, ForceId force)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new EntityInfo { ForceId = force });
            return e;
        }

        // one solve of a 100 m ear at `ear`, with `source` in the grid; how many answers
        private static int Solve(EntityRepository w, Vector3 ear, Entity? source = null)
        {
            var unit = Man(w, ear, ForceId.Friend);
            var s = w.CreateEntity();
            var entry = new SensorEntryDto { Acoustic = new AcousticSensorDto { Range = 100f } };
            w.SetManagedComponent(s, new SensorCapability { Default = entry, Current = entry });
            w.SetSingleton(new GlobalTime { TotalTime = 0.1 });   // a solve hears what was made before its own time
            var grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            try
            {
                grid.Clear();
                if (source is { } src)
                {
                    var p = w.GetComponentRO<SimTransform>(src).Position;
                    grid.Add(src, new Vector2(p.X, p.Y));
                }
                var sensor = new EqsSensor { ContextSlot0 = unit };
                Span<EqsResult> c = stackalloc EqsResult[8];
                return new AcousticSensorGenerator(grid).Generate(s, ref sensor, w, c);
            }
            finally { grid.Dispose(); }
        }

        // a short sound of `cls` made at `at`, carrying `carry`, heard at `ear`?
        private static bool Heard(bool house, Vector3 at, Vector3 ear, float carry, SoundSourceClass cls)
        {
            using var w = World(house);
            var maker = Man(w, at, ForceId.Hostile);
            SoundEmissionRing.Of(w).Append(new SoundEmission { Source = maker.PackedValue, Force = (byte)ForceId.Hostile,
                Kind = (byte)SoundKind.Movement, Class = (byte)cls, At = at, Carry = carry });
            return Solve(w, ear) == 1;
        }

        /// <summary>⭐ AQ104 E ② — footsteps inside the house, 13 m from a man outside its west wall: a 20 m carry is heard in the open,
        /// not through the wall (halved to 10 m); a 30 m one still is (15 m — a halving, not a wall of silence). ✅ Red-proof: the wall
        /// line removed from <c>AcousticSensorGenerator.Hear</c> ⇒ the 20 m steps heard through the wall ⇒ red.</summary>
        [Fact]
        public void AQ104E_AWallBetween_HalvesHowFarFootstepsCarry()
        {
            var inside = new Vector3(5f, 5f, 0f);
            var outside = new Vector3(-8f, 5f, 0f);
            Assert.True(Heard(false, inside, outside, 20f, SoundSourceClass.Footsteps));
            Assert.False(Heard(true, inside, outside, 20f, SoundSourceClass.Footsteps));
            Assert.True(Heard(true, inside, outside, 30f, SoundSourceClass.Footsteps));
        }

        /// <summary>⭐ AQ104 E ② — the slab: steps upstairs, 3 m over a man downstairs, carrying 5 m — halved to 2.5 m, not heard; 8 m —
        /// heard.</summary>
        [Fact]
        public void AQ104E_ASlabBetween_HalvesHowFarStepsUpstairsCarry()
        {
            var below = new Vector3(5f, 5f, 0f);
            var above = new Vector3(5f, 5f, 3f);
            Assert.False(Heard(true, above, below, 5f, SoundSourceClass.Footsteps));
            Assert.True(Heard(true, above, below, 8f, SoundSourceClass.Footsteps));
        }

        /// <summary>⭐ AQ104 E ② (Q95 B3) — the band: an explosion (Low, × 0.2 of the wall's dB) carrying 15 m through the same wall as the
        /// 20 m steps keeps ~87 % of its carry (13 m away) — heard.</summary>
        [Fact]
        public void AQ104E_ALowSound_PassesTheWall()
        {
            Assert.True(Heard(true, new Vector3(5f, 5f, 0f), new Vector3(-8f, 5f, 0f), 15f, SoundSourceClass.Explosion));
            Assert.Equal(1f, AcousticPerception.WallFactor(null, Vector3.Zero, Vector3.One, (byte)SoundSourceClass.Footsteps));
        }

        /// <summary>⭐ AQ106 (R-3050) F — at the SENSOR: steps in house-2f's west room, a man in the east room, the hall door open; an 8 m carry
        /// is heard (6.5 m away, round through the door) though the straight line alone (the inner wall's halving) would stop it at 4 m, and
        /// the answer is placed at the DOORWAY, not on the steps. ✅ Red-proof: the answer at the source (<c>where</c>) ⇒ red.</summary>
        [Fact]
        public void AQ106_ASoundRoundTheCorner_IsHeard_FromTheDoorway()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors"))) dir = dir.Parent;
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            string json = "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-50,-50,50,50],\"groundZ\":0},\"features\":[" +
                          "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"template\":\"house-2f\",\"label\":\"H\",\"baseZ\":0," +
                          "\"rotation\":0,\"doors\":{\"hall\":\"open\"}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0]}}]}";
            using var w = World(house: false);
            w.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(json, "h", Fdp.Toolkit.Terrain.TerrainAssets.ForFolder(folder)));
            var source = new Vector3(2f, 6.6f, 0f);
            var maker = Man(w, source, ForceId.Hostile);
            SoundEmissionRing.Of(w).Append(new SoundEmission { Source = maker.PackedValue, Force = (byte)ForceId.Hostile,
                Kind = (byte)SoundKind.Movement, Class = (byte)SoundSourceClass.Footsteps, At = source, Carry = 8f });
            var unit = Man(w, new Vector3(8f, 4f, 0f), ForceId.Friend);
            var s = w.CreateEntity();
            var entry = new SensorEntryDto { Acoustic = new AcousticSensorDto { Range = 100f } };
            w.SetManagedComponent(s, new SensorCapability { Default = entry, Current = entry });
            w.SetSingleton(new GlobalTime { TotalTime = 0.1 });
            var grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            try
            {
                var sensor = new EqsSensor { ContextSlot0 = unit };
                Span<EqsResult> c = stackalloc EqsResult[8];
                Assert.Equal(1, new AcousticSensorGenerator(grid).Generate(s, ref sensor, w, c));
                var heardAt = new Vector2(c[0].PositionX, c[0].PositionY);
                Assert.True(Vector2.Distance(heardAt, new Vector2(5f, 6.6f)) <= c[0].Score + 0.1f, $"answered at {heardAt}, radius {c[0].Score}");
                Assert.True(Vector2.Distance(heardAt, new Vector2(source.X, source.Y)) > 1.5f, $"answered on the steps themselves: {heardAt}");
            }
            finally { grid.Dispose(); }
        }

        /// <summary>⭐ AQ106 L — the debug explain is a dry run of the SAME sensor: in house-2f, steps round the open hall door (heard, via the
        /// openings, waypoints through the doorway), a shot 300 m off carrying 100 m (beyond its carry), a friend's steps (his own side); and
        /// what it calls heard is exactly what the sensor answered. ✅ Red-proof: Explain judging without the walls ⇒ reasons disagree ⇒ red.</summary>
        [Fact]
        public void AQ106L_ExplainHearing_GivesEachSoundsVerdict_AsTheSensorHeardIt()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors"))) dir = dir.Parent;
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            string json = "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-500,-500,500,500],\"groundZ\":0},\"features\":[" +
                          "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"template\":\"house-2f\",\"label\":\"H\",\"baseZ\":0," +
                          "\"rotation\":0,\"doors\":{\"hall\":\"open\"}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0]}}]}";
            using var w = World(house: false);
            w.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(json, "h", Fdp.Toolkit.Terrain.TerrainAssets.ForFolder(folder)));
            var ring = SoundEmissionRing.Of(w);
            var steps = Man(w, new Vector3(2f, 6.6f, 0f), ForceId.Hostile);
            ring.Append(new SoundEmission { Source = steps.PackedValue, Force = (byte)ForceId.Hostile, Kind = (byte)SoundKind.Movement,
                Class = (byte)SoundSourceClass.Footsteps, At = new Vector3(2f, 6.6f, 0f), Carry = 8f });
            var far = Man(w, new Vector3(300f, 0f, 0f), ForceId.Hostile);
            ring.Append(new SoundEmission { Source = far.PackedValue, Force = (byte)ForceId.Hostile, Kind = (byte)SoundKind.Shot,
                Class = (byte)SoundSourceClass.SmallArms, At = new Vector3(300f, 0f, 0f), Carry = 100f });
            var friend = Man(w, new Vector3(9f, 6f, 0f), ForceId.Friend);
            ring.Append(new SoundEmission { Source = friend.PackedValue, Force = (byte)ForceId.Friend, Kind = (byte)SoundKind.Movement,
                Class = (byte)SoundSourceClass.Footsteps, At = new Vector3(9f, 6f, 0f), Carry = 20f });
            var unit = Man(w, new Vector3(8f, 4f, 0f), ForceId.Friend);
            var s = w.CreateEntity();
            var entry = new SensorEntryDto { Acoustic = new AcousticSensorDto { Range = 100f } };
            w.SetManagedComponent(s, new SensorCapability { Default = entry, Current = entry });
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            w.AddComponent(s, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = unit });
            w.SetSingleton(new GlobalTime { TotalTime = 0.1 });

            var list = new System.Collections.Generic.List<HeardSoundExplanation>();
            Assert.True(HearingExplain.TryExplain(w, unit, 10, list, out var why), why);
            var byFrom = list.ToDictionary(x => x.Source);
            Assert.Equal(HearingReason.Heard, byFrom[steps.PackedValue].Verdict.Reason);
            Assert.Equal(SoundVia.Openings, byFrom[steps.PackedValue].Verdict.Route.Via);
            Assert.Contains(byFrom[steps.PackedValue].Waypoints, p => Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(5f, 6.6f)) < 0.3f);
            Assert.Equal(HearingReason.BeyondItsCarry, byFrom[far.PackedValue].Verdict.Reason);
            Assert.Equal(HearingReason.HisOwnSide, byFrom[friend.PackedValue].Verdict.Reason);

            var grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            try
            {
                var sensor = new EqsSensor { ContextSlot0 = unit };
                Span<EqsResult> c = stackalloc EqsResult[8];
                Assert.Equal(list.Count(x => x.Verdict.Reason == HearingReason.Heard), new AcousticSensorGenerator(grid).Generate(s, ref sensor, w, c));
            }
            finally { grid.Dispose(); }
        }

        /// <summary>⭐ AQ105 D (R-2080) — a SHOUT: a hostile challenging 30 m off is heard (no emitter needed); talk is not; 50 m is past
        /// the voice's 40 m; his own side never. ✅ Red-proof: the voice line removed from <c>AcousticSensorGenerator.Generate</c> ⇒ the
        /// challenge unheard ⇒ red.</summary>
        [Theory]
        [InlineData(UtteranceKind.Challenge, 30f, ForceId.Hostile, true)]
        [InlineData(UtteranceKind.CallManDown, 30f, ForceId.Hostile, true)]
        [InlineData(UtteranceKind.Talk, 30f, ForceId.Hostile, false)]
        [InlineData(UtteranceKind.Challenge, 50f, ForceId.Hostile, false)]
        [InlineData(UtteranceKind.Challenge, 30f, ForceId.Friend, false)]
        public void AQ105D_AShout_IsHeardByTheEnemyInEarshot(UtteranceKind kind, float distance, ForceId side, bool heard)
        {
            using var w = World(house: false);
            var speaker = Man(w, new Vector3(distance, 0f, 0f), side);
            w.AddComponent(speaker, new Utterance { Kind = kind, Until = 5.0 });
            Assert.Equal(heard ? 1 : 0, Solve(w, Vector3.Zero, speaker));
        }

        /// <summary>⭐ AQ105 D — the shouted kinds: everything but talk, and nothing when silent.</summary>
        [Fact]
        public void AQ105D_EveryKindButTalk_IsShouted()
        {
            foreach (UtteranceKind k in Enum.GetValues(typeof(UtteranceKind)))
                Assert.Equal(k is not (UtteranceKind.UtterNone or UtteranceKind.Talk), Utterances.IsShouted(k));
        }
    }
}

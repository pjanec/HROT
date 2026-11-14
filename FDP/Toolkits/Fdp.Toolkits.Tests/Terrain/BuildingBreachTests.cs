using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>⭐ <c>CE-4391</c> S-1 — test helper: building-damage ENTITIES in a world, the way production holds them (UTM §8.2 B-2), read back through <see cref="DoorStates.Damage"/>.</summary>
    internal static class BreachFixtures
    {
        /// <summary>A brick house 10 × 8 m, one 3 m storey, no doors, at (20, 20): the south wall is panel <see cref="SouthWall"/> (x 20..30, y 20).</summary>
        public const string BrickHouse = """
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                      { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        public static TerrainWorld House() => TerrainWorldParser.Parse(BrickHouse, "range");

        /// <summary>The house with its own <c>collapseMinKg</c> (a rail that must collapse — or must never collapse — states it, instead of leaning on the shared default, 80 kg).</summary>
        public static TerrainWorld HouseCollapsingAt(float kg)
            => TerrainWorldParser.Parse(BrickHouse, "range", new TerrainAssets { Breaches = BreachTable.Shared.WithOverrides("{ \"collapseMinKg\": " + kg.ToString(System.Globalization.CultureInfo.InvariantCulture) + " }", "test") });

        /// <summary>The brick house with a closed door "front" in its south wall (x 24.5 .. 25.5).</summary>
        public const string DoorHouse = """
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H", "doors": { "front": "closed" },
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "openings": [ { "kind": "door", "at": 4.5, "width": 1, "doorId": "front" } ] },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3 }, { "from": [10,8], "to": [0,8], "thickness": 0.3 }, { "from": [0,8], "to": [0,0], "thickness": 0.3 } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        public static Entity Collapsed(EntityRepository repo, TerrainWorld terrain, int building)
        {
            var e = Damage(repo, terrain, building);
            var d = repo.GetComponentRO<BuildingDamage>(e); d.Collapsed = 1; repo.SetComponent(e, d);
            return e;
        }

        /// <summary>Index of the house's south wall (y = 20, running west → east).</summary>
        public static int SouthWall(TerrainWorld w)
            => Array.FindIndex(w.Panels.ToArray(), p => MathF.Abs(p.A.Y - 20f) < 1e-3f && MathF.Abs(p.B.Y - 20f) < 1e-3f && p.B.X > p.A.X);

        public static EntityRepository World(TerrainWorld terrain)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<BuildingDamage>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            repo.SetSingletonManaged(terrain);
            return repo;
        }

        public static Entity Damage(EntityRepository repo, TerrainWorld terrain, int building, params WallBreach[] breaches)
        {
            var d = new BuildingDamage { Building = building };
            foreach (var b in breaches) BuildingDamages.Add(ref d, in b, null);
            var e = repo.CreateEntity();
            repo.AddComponent(e, d);
            repo.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(terrain, building) });
            return e;
        }

        public static TerrainWallPanel Panel(string material = "concrete", float thickness = 0.3f)
            => new()
            {
                A = new Vector2(0, 0), B = new Vector2(10, 0), Thickness = thickness, BaseZ = 0f, TopZ = 3f,
                Material = TerrainMaterialLibrary.Shared.Get(material, "test"), Building = 0, Storey = 0,
            };
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-1 (docs/DESIGN_Terrain_Import_UTM.md §8 D-1…D-6, §8.2 B-1…B-8) — destructible buildings, breaches: the rule (a charge, a shell,
    /// a bomb, by scaled distance and material), the per-building damage list (merge, capacity), the view table every sight / fire reader gets on
    /// <see cref="DoorStates.Damage"/>, the traces that pass a breach, and the hot-path rails (0 bytes per call).
    /// </summary>
    public sealed class BuildingBreachTests
    {
        // ── the rule: BreachRule over the data table ───────────────────────────────────────────────────────────

        /// <summary>🔴 Red-proof: drop the row's <c>fuzes</c> filter ⇒ the impact rails below turn red; set the row's <c>passage</c> to false in <c>breaches.json</c> ⇒ the bottom is no longer the floor and this names it.</summary>
        [Fact]
        public void Rule_ATimeFuzedChargeAtContactOnABrickWall_IsAPassage_AboutOneByTwoMetres()
        {
            var panel = BreachFixtures.Panel("brick");
            var table = BreachTable.Shared;
            // 8 kg against the wall's face (the wall's face is y = -0.15): d = 0.1 m, Z = 0.05
            Assert.True(BreachRule.TryDecide(table, panel, 7, new Vector3(5f, -0.25f, 1.0f), 8f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Time, out var b));
            Assert.Equal(BreachKind.Passage, b.Kind);
            Assert.Equal(7, b.Panel);
            Assert.Equal(0f, b.Bottom);                                  // on the floor
            Assert.InRange(b.Width, 1.0f, 1.5f);                         // 1 m × √(1500 / 1000) = 1.22
            Assert.InRange(b.Height, 2.0f, 2.6f);                        // 2 m × 1.22 = 2.45
            Assert.Equal(5f, (b.From + b.To) / 2f, 3);                   // centred on the burst's projection on the wall
        }

        /// <summary>🔴 Red-proof: drop the <c>MathF.Cbrt</c> mass scaling in <c>TryDecide</c> ⇒ the 3.4 kg shell makes the 8 kg size.</summary>
        [Fact]
        public void Rule_ATankShellAtOneMetre_IsAHole_OfTheTableSize()
        {
            var panel = BreachFixtures.Panel("concrete");
            // 125 mm HE-frag, 3.4 kg, 1 m off the face (the face is y = -0.15) and 1.5 m up: Z = 1.0 / ∛3.4 = 0.67 ⇒ the "shell close by" row
            Assert.True(BreachRule.TryDecide(BreachTable.Shared, panel, 0, new Vector3(5f, -1.15f, 1.5f), 3.4f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out var b));
            Assert.Equal(BreachKind.Hole, b.Kind);
            float scale = MathF.Cbrt(3.4f / 8f);
            Assert.Equal(2.0f * scale, b.Width, 2);                      // 1.50 m
            Assert.Equal(1.6f * scale, b.Height, 2);                     // 1.20 m — inside D-5's 1–1.5 m for a tank
            Assert.Equal(1.5f, (b.Bottom + b.Top) / 2f, 3);              // centred on the burst's height
            Assert.True(b.Bottom > 0.3f);                                // clear of the floor: not snapped
        }

        /// <summary>
        /// ⭐ The REAL case: an IMPACT round bursts <c>AreaEffect.BurstStandOffMetres</c> (0.1 m) off the wall it struck, so Z ≈ 0 — the doorway row (a placed or
        /// time-fuzed charge) must NOT catch it. A tank HE (3.4 kg) is a ~1.5 × 1.2 m HOLE centred on the burst; a 155 mm (6.6 kg) a ~1.9 × 1.5 m hole.
        /// 🔴 Red-proof: remove <c>"fuzes": ["time"]</c> from the charge row in <c>breaches.json</c> ⇒ both become floor doorways (Passage / bottom 0).
        /// </summary>
        [Fact]
        public void Rule_AnImpactRoundAtTheWall_IsAHole_NeverADoorway()
        {
            var panel = BreachFixtures.Panel("concrete");          // the face is y = -0.15; the burst stands 0.1 m off it
            var at = new Vector3(5f, -0.25f, 1.5f);
            Assert.True(BreachRule.TryDecide(BreachTable.Shared, panel, 0, at, 3.4f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out var tank));
            Assert.Equal(BreachKind.Hole, tank.Kind);
            Assert.Equal(2.0f * MathF.Cbrt(3.4f / 8f), tank.Width, 2);   // 1.50
            Assert.Equal(1.6f * MathF.Cbrt(3.4f / 8f), tank.Height, 2);  // 1.20
            Assert.Equal(1.5f, (tank.Bottom + tank.Top) / 2f, 3);        // centred on the burst
            Assert.Equal(5f, (tank.From + tank.To) / 2f, 3);
            Assert.True(BreachRule.TryDecide(BreachTable.Shared, panel, 0, at, 6.6f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out var how));
            Assert.Equal(BreachKind.Hole, how.Kind);
            Assert.InRange(how.Width, 1.8f, 2.0f);                       // 1.88
            Assert.InRange(how.Height, 1.4f, 1.6f);                      // 1.50
            Assert.True(how.Bottom > 0.3f);
        }

        [Fact]
        public void Rule_ASizeScalesWithTheMaterial_WoodBreaksWider_AndSteelNarrower_Clamped()
        {
            var table = BreachTable.Shared;
            var at = new Vector3(5f, -1.15f, 1.5f);
            Assert.True(BreachRule.TryDecide(table, BreachFixtures.Panel("concrete"), 0, at, 3.4f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out var concrete));
            Assert.True(BreachRule.TryDecide(table, BreachFixtures.Panel("fence-wood"), 0, at, 3.4f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out var wood));
            Assert.True(BreachRule.TryDecide(table, BreachFixtures.Panel("steel-plate"), 0, at, 3.4f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out var steel));
            Assert.Equal(concrete.Width * 2f, wood.Width, 2);            // √(1500 / 60) = 5, clamped to 2
            Assert.Equal(concrete.Width * MathF.Sqrt(1.5f), steel.Width, 2);   // √(1500 / 1000)
            Assert.Equal(0.5f, BreachRule.MaterialScale(table, new TerrainMaterial("vault", 0f, 100000f, 0f, true)));   // clamped to 0.5
        }

        [Fact]
        public void Rule_AKineticRound_ATooSmallCharge_AndABurstFarAway_MakeNothing()
        {
            var panel = BreachFixtures.Panel("brick");
            var table = BreachTable.Shared;
            var near = new Vector3(5f, -0.25f, 1.0f);
            Assert.False(BreachRule.TryDecide(table, panel, 0, near, 0f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out _));                             // kinetic: no explosive
            Assert.False(BreachRule.TryDecide(table, panel, 0, near, 0.24f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out _));                          // an M67 grenade: below minKg
            Assert.False(BreachRule.TryDecide(table, panel, 0, new Vector3(5f, -30f, 1.5f), 6.6f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out _));    // a 155 mm 30 m away: Z = 16
            Assert.Equal(0f, BreachRule.Reach(table, 0.2f));
            Assert.Equal(table.MaxZ * MathF.Cbrt(6.6f), BreachRule.Reach(table, 6.6f));
        }

        [Fact]
        public void Rule_AHeavyBombClose_BreachesTheWholePanelOfTheStorey()
        {
            var panel = BreachFixtures.Panel("concrete");
            Assert.True(BreachRule.TryDecide(BreachTable.Shared, panel, 0, new Vector3(5f, -2f, 1.5f), 94f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Impact, out var b));   // a Mk-82 (94 kg), 2 m off
            Assert.Equal(0f, b.From); Assert.Equal(10f, b.To);
            Assert.Equal(0f, b.Bottom); Assert.Equal(3f, b.Top);
            Assert.Equal(BreachKind.Passage, b.Kind);
        }

        [Fact]
        public void Rule_IsClippedToThePanel_ABurstBesideTheEndMakesAShorterBreach_AndOneBeyondItNone()
        {
            var panel = BreachFixtures.Panel("concrete");
            var table = BreachTable.Shared;
            Assert.True(BreachRule.TryDecide(table, panel, 0, new Vector3(0.3f, -0.25f, 1.0f), 8f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Time, out var b));
            Assert.Equal(0f, b.From);                                    // clipped at the A end
            Assert.True(b.To < 1.0f);
            Assert.False(BreachRule.TryDecide(table, panel, 0, new Vector3(-3f, -0.25f, 1.0f), 8f, Fdp.Toolkit.Tkb.Domain.FuzeKind.Time, out _));   // 3 m past the end: nothing of the panel left
        }

        /// <summary>The scaled-distance rows are DATA: a terrain's own table replaces the keys it names (UTM §8.2 B-6, as the material library).</summary>
        [Fact]
        public void Table_ATerrainOverride_ReplacesTheKeysItNames_AndTheRestStays()
        {
            var shared = BreachTable.Shared;
            var over = shared.WithOverrides("""{ "minKg": 5, "rows": [ { "zMax": 0.5, "width": 3, "height": 3, "passage": true } ] }""", "test");
            Assert.Equal(5f, over.MinKg);
            Assert.Single(over.Rows);
            Assert.Equal(shared.ReferenceKg, over.ReferenceKg);
            Assert.NotEqual(shared.MinKg, over.MinKg);
            Assert.Throws<ArgumentException>(() => shared.WithOverrides("""{ "rows": [ { "zMax": 1 } ] }""", "bad"));   // a row with no size
        }

        // ── the per-building damage list: merge, capacity ──────────────────────────────────────────────────────

        private static WallBreach Hole(int panel, float from, float to, float bottom = 1f, float top = 1.5f)
            => new(panel, from, to, bottom, top, BreachKind.Hole);

        /// <summary>🔴 Red-proof: make <c>Overlaps</c> ignore the Z interval ⇒ the "same place, different height" breach is merged and Count is 1 not 2.</summary>
        [Fact]
        public void Add_AnOverlappingBreach_IsMerged_ADisjointOneAppended_ASecondShellInTheSameHoleChangesNothing()
        {
            var d = new BuildingDamage { Building = 0 };
            Assert.Equal(BreachAdd.Added, BuildingDamages.Add(ref d, Hole(0, 1f, 2f), null));
            Assert.Equal(BreachAdd.Merged, BuildingDamages.Add(ref d, Hole(0, 1.5f, 3f), null));
            Assert.Equal(1, d.Count);
            Assert.Equal(Hole(0, 1f, 3f), d.Get(0));                                    // the union rectangle
            Assert.Equal(BreachAdd.Added, BuildingDamages.Add(ref d, Hole(0, 1.5f, 3f, bottom: 2.4f, top: 2.8f), null));   // same along, higher: disjoint
            Assert.Equal(BreachAdd.Added, BuildingDamages.Add(ref d, Hole(1, 1.5f, 3f), null));                            // another panel
            Assert.Equal(3, d.Count);

            var before = d;
            Assert.Equal(BreachAdd.Unchanged, BuildingDamages.Add(ref d, Hole(0, 1.2f, 2.5f, 1.1f, 1.4f), null));         // inside the first
            Assert.Equal(before.Count, d.Count);
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<BuildingDamage>(in d))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<BuildingDamage>(in before))));
        }

        [Fact]
        public void Add_ABridgingBreach_MergesEverythingItTouches_AndTheSeventeenthDisjointOneDoesNotFit()
        {
            var d = new BuildingDamage { Building = 0 };
            for (int i = 0; i < 16; i++) Assert.Equal(BreachAdd.Added, BuildingDamages.Add(ref d, Hole(0, i * 0.5f, i * 0.5f + 0.3f), null));
            Assert.Equal(16, d.Count);
            var full = d;
            Assert.Equal(BreachAdd.Full, BuildingDamages.Add(ref d, Hole(0, 20f, 20.3f), null));   // no room: the owner opens a second entity
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<BuildingDamage>(in d))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<BuildingDamage>(in full))));   // atomic: untouched

            // one breach spanning the whole row absorbs all sixteen
            Assert.Equal(BreachAdd.Merged, BuildingDamages.Add(ref d, Hole(0, 0f, 8f), null));
            Assert.Equal(1, d.Count);
            Assert.Equal(0f, d.Get(0).From); Assert.Equal(8f, d.Get(0).To);
        }

        [Fact]
        public void Add_ATwoHolesUnion_IsReclassified_ItBecomesAPassageWhenItReachesTheFloorAndIsBigEnough()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var d = new BuildingDamage { Building = 0 };
            BuildingDamages.Add(ref d, new WallBreach(south, 4f, 4.6f, 0f, 2.0f, BreachKind.Hole), w);   // 0.6 wide: a hole
            Assert.Equal(BreachKind.Hole, d.Get(0).Kind);
            BuildingDamages.Add(ref d, new WallBreach(south, 4.5f, 5.3f, 0f, 2.0f, BreachKind.Hole), w);
            Assert.Equal(1, d.Count);
            Assert.Equal(BreachKind.Passage, d.Get(0).Kind);                                              // 1.3 × 2.0 on the floor
        }

        // ── the view: the table every reader gets on DoorStates.Damage ─────────────────────────────────────────

        [Fact]
        public void Table_OpensOnlyInsideTheRectangle_AndUnionsEveryEntityOfTheView()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = BreachFixtures.World(w);
            Assert.Null(DoorStates.Of(repo, w).Damage);                                                  // nothing damaged: no table, no cost
            BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage));
            BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 8f, 9f, 1f, 1.6f, BreachKind.Hole));   // a second entity (another deciding node)

            var t = DoorStates.Of(repo, w).Damage!;
            Assert.NotNull(t);
            Assert.Equal(2, t.Count);
            Assert.Equal(1, t.PassageCount);
            Assert.True(t.HasBreaches(south));
            Assert.False(t.HasBreaches(south + 1));
            Assert.True(t.Opens(south, 5f, 1f));
            Assert.True(t.Opens(south, 8.5f, 1.2f));                                                     // the other entity's
            Assert.False(t.Opens(south, 5f, 2.5f));                                                      // above the first
            Assert.False(t.Opens(south, 7f, 1f));                                                        // between them
            Assert.False(t.Opens(south + 1, 5f, 1f));                                                    // another panel
            Assert.False(t.Opens(-1, 5f, 1f));
            Assert.Equal(2, t.BreachesOf(south).Length);
        }

        /// <summary>
        /// 🔴 Red-proof: have <c>CrossingBreached</c> in <c>TerrainWorld</c> return false ⇒ the first assertion after the damage fails (the line is still blocked);
        /// have it ignore <c>Opens</c> and return <c>HasBreaches</c> ⇒ the "next to the breach" line passes and that assertion names it.
        /// </summary>
        [Fact]
        public void Sight_ALineThroughABreach_PassesAtOnce_TheSameWallBesideItStaysBlocked_AndFireMeetsNoPanel()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = BreachFixtures.World(w);
            var through = (From: new Vector3(25f, 15f, 1.5f), To: new Vector3(25f, 25f, 1.5f));    // along 5 m of the south wall
            var beside  = (From: new Vector3(22f, 15f, 1.5f), To: new Vector3(22f, 25f, 1.5f));    // along 2 m
            var above   = (From: new Vector3(25f, 15f, 2.6f), To: new Vector3(25f, 25f, 2.6f));    // along 5 m, over the breach's top (2.2)
            var fire = new List<TerrainWorld.FireCrossing>();

            var before = DoorStates.Of(repo, w);
            Assert.True(w.SegmentBlocked(through.From, through.To, before));
            w.QueryFire(through.From, through.To, fire, before);
            Assert.Contains(fire, c => c.Kind == "panel");

            BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage));
            var after = DoorStates.Of(repo, w);
            Assert.NotNull(after.Damage);
            Assert.False(w.SegmentBlocked(through.From, through.To, after));                         // the line passes
            Assert.True(w.SegmentBlocked(beside.From, beside.To, after));                            // the wall beside the breach still stops it
            Assert.True(w.SegmentBlocked(above.From, above.To, after));                              // …and the wall above it
            w.QueryFire(through.From, through.To, fire, after);
            Assert.DoesNotContain(fire, c => c.Kind == "panel");                                     // a round meets no panel in the hole
            w.QueryFire(beside.From, beside.To, fire, after);
            Assert.Contains(fire, c => c.Kind == "panel");
            Assert.Empty(w.QuerySight(through.From, through.To, after).Crossed.Where(c => c.Kind == "panel"));
            Assert.NotEmpty(w.QuerySight(beside.From, beside.To, after).Crossed.Where(c => c.Kind == "panel"));
            Assert.True(w.SegmentBlocked(through.From, through.To, before));                         // a table handed out earlier is untouched
        }

        /// <summary>
        /// 🔴 Red-proof: restore the gate to <c>world.Doors.Count &gt; 0</c> in <c>TerrainWorldQuery.ForView</c> ⇒ the query for this door-less house carries no
        /// <c>DoorStates</c> and the breach is invisible to every perception caller.
        /// </summary>
        [Fact]
        public void TheGates_ADoorlessTerrainWithABuilding_StillBuildsADoorStates_SoItsBreachesAreSeen()
        {
            var w = BreachFixtures.House();
            Assert.Empty(w.Doors);
            Assert.True(w.HasLiveState);
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(BreachFixtures.SouthWall(w), 4f, 6f, 0f, 2.2f, BreachKind.Passage));

            Assert.NotNull(TerrainWorldQuery.ForView(repo, w).Doors?.Damage);
            Assert.NotNull(DoorStates.Of(repo)?.Damage);                                              // the one-argument overload too

            var open = new List<Fdp.Toolkit.World.TraceCrossing>();
            TerrainWorldQuery.ForView(repo, w).Trace(new Vector3(25f, 15f, 1.5f), new Vector3(25f, 25f, 1.5f), Fdp.Toolkit.World.TracePurpose.Fire, open);
            Assert.DoesNotContain(open, c => c.Kind == "panel" || c.ClosedBarrier);
        }

        [Fact]
        public void Table_IsRebuiltOnlyWhenTheDamageChanges_AndAHandedOutTableNeverChanges()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = BreachFixtures.World(w);
            var e = BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage));

            var a = DoorStates.Of(repo, w);
            Assert.Same(a, DoorStates.Of(repo, w));                                                   // unchanged ⇒ the same instance
            repo.Tick();
            Assert.Same(a, DoorStates.Of(repo, w));                                                   // …also on a later frame

            var d = repo.GetComponentRO<BuildingDamage>(e);
            BuildingDamages.Add(ref d, new WallBreach(south, 8f, 9f, 1f, 1.6f, BreachKind.Hole), w);
            repo.SetComponent(e, d);                                                                   // written in the SAME tick: seen at once
            var b = DoorStates.Of(repo, w);
            Assert.NotSame(a, b);
            Assert.Equal(2, b.Damage!.Count);
            Assert.Equal(1, a.Damage!.Count);                                                         // the held table keeps what it was built with
            Assert.Same(b, DoorStates.Of(repo, w));

            repo.DestroyEntity(e);                                                                     // the entity goes: the breach goes with it
            Assert.Null(DoorStates.Of(repo, w).Damage);
        }

        /// <summary>
        /// ⭐⭐ B-8 / R-220 — the hot path: a sight, a fire trace and a door-table ask through a breached wall allocate NOTHING once warm, and the table is the
        /// same instance on an unchanged frame. 🔴 Red-proof: have <c>BuildingDamageTable.Opens</c> build a list per call ⇒ the byte count below is not 0.
        /// </summary>
        [Fact]
        public void HotPath_ThroughABreachedWall_AllocatesNothing_AndTheTableIsTheSameInstance()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage), new WallBreach(south, 8f, 9f, 1f, 1.6f, BreachKind.Hole));
            var fire = new List<TerrainWorld.FireCrossing>(8);
            var lines = new[]
            {
                (new Vector3(25f, 15f, 1.5f), new Vector3(25f, 25f, 1.5f)),   // through the passage
                (new Vector3(28.5f, 15f, 1.2f), new Vector3(28.5f, 25f, 1.2f)), // through the hole
                (new Vector3(22f, 15f, 1.5f), new Vector3(22f, 25f, 1.5f)),   // beside them: blocked
                (new Vector3(15f, 24f, 1.5f), new Vector3(35f, 24f, 1.5f)),   // across the house, no wall on the way
            };
            DoorStates doors = DoorStates.Of(repo, w);
            bool anyPassed = false, anyBlocked = false;
            void Run()
            {
                doors = DoorStates.Of(repo, w);
                foreach (var (a, b) in lines)
                {
                    bool blocked = w.SegmentBlocked(a, b, doors);
                    anyPassed |= !blocked; anyBlocked |= blocked;
                    w.QueryFire(a, b, fire, doors);
                }
            }
            Run(); Run();   // warm-up: the thread's scratch, the lazy lists

            var first = doors;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(anyPassed && anyBlocked);
            Assert.Equal(0L, allocated);
            Assert.Same(first, doors);
        }


        // ── CE-4598 (B-1; docs/DESIGN_Vehicle_Breaching.md §7 V-1, V-5, D-4) — the HULL purpose of the trace ─────────────────────────

        /// <summary>The brick house plus a 1 m brick garden wall along y = 10 from x = 0 to 40.</summary>
        private static TerrainWorld HouseAndGardenWall() => TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                      { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } },
              { "type": "Feature", "properties": { "kind": "wall", "label": "garden", "height": 1.0, "thickness": 0.3, "material": "brick" },
                "geometry": { "type": "LineString", "coordinates": [[0,10],[40,10]] } } ] }
            """, "range");

        private static int GardenPanel(TerrainWorld w) => Array.FindIndex(w.Panels.ToArray(), p => p.Building < 0);

        private static bool HullBlocked(TerrainWorldQuery q, Vector3 a, Vector3 b, List<Fdp.Toolkit.World.TraceCrossing> into)
        {
            q.Trace(a, b, Fdp.Toolkit.World.TracePurpose.Hull, into);
            foreach (var c in into) if (c.ClosedBarrier) return true;
            return false;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4598</c> (B-1, V-1, D-4) — the hull purpose MEETS a free wall the fire purpose's barrier meaning ignores (W-7′: a blast
        /// diffracts round a garden wall), at the hull's probe height 0.5 m above the ground and even 0.4 m over a 1 m wall's top (the hull's
        /// body below the line); a line 0.6 m over it passes. 🔴 Red-proof: <c>IsHullBarrier</c> = <c>IsClosedBarrier</c> ⇒ the first
        /// assertion fails; drop the band in <c>QueryFireCore</c> ⇒ the "over its top" assertion fails.
        /// </summary>
        [Fact]
        public void Hull_AFreeWall_BlocksAHull_ButIsNoClosedBarrierForABlast()
        {
            var w = HouseAndGardenWall();
            var q = new TerrainWorldQuery(w, DoorStates.Of(new EntityRepository(), w));
            var into = new List<Fdp.Toolkit.World.TraceCrossing>();
            Assert.True(HullBlocked(q, new Vector3(5f, 5f, 0.5f), new Vector3(5f, 15f, 0.5f), into), "a hull's line at 0.5 m");
            q.Trace(new Vector3(5f, 5f, 0.5f), new Vector3(5f, 15f, 0.5f), Fdp.Toolkit.World.TracePurpose.Fire, into);
            Assert.All(into, c => Assert.False(c.ClosedBarrier));                                          // the blast meaning is unchanged
            Assert.True(HullBlocked(q, new Vector3(5f, 5f, 1.4f), new Vector3(5f, 15f, 1.4f), into), "a line 0.4 m over its top: the hull's body meets it");
            Assert.False(HullBlocked(q, new Vector3(5f, 5f, 1.6f), new Vector3(5f, 15f, 1.6f), into), "a line 0.6 m over its top passes");
            Assert.Equal(0f, w.HullSurfaceZ(5f, 10f, 0.5f), 3);                                            // its top (within a step of 0.5) is never a hull's surface
            Assert.Equal(1f, w.SurfaceZ(5f, 10f, 0.5f), 3);                                                // a man's surface is unchanged
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4598</c> (B-1, V-5) — a breach opens a FREE wall for a hull when it is open from the wall's base, and NEVER opens a
        /// building's wall for a hull (R-3025: it never enters a building) — while sight and fire pass both. 🔴 Red-proof: in
        /// <c>HullBreachOpen</c> drop the <c>Building &gt;= 0</c> test ⇒ the building assertion fails; test only the line's height ⇒ the
        /// raised-hole assertion fails.
        /// </summary>
        [Fact]
        public void Hull_AFreeWallBreachOpens_ABuildingBreachNever_AndARaisedHoleNever()
        {
            var w = HouseAndGardenWall();
            int south = BreachFixtures.SouthWall(w), garden = GardenPanel(w);
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Damage(repo, w, 0,
                new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage),
                new WallBreach(garden, 4f, 8f, 0f, 1.0f, BreachKind.Passage),     // x 4..8 of the garden wall, base to top
                new WallBreach(garden, 20f, 24f, 0.3f, 1.0f, BreachKind.Hole));   // x 20..24, a hole above 0.3 m
            var q = new TerrainWorldQuery(w, DoorStates.Of(repo, w));
            var into = new List<Fdp.Toolkit.World.TraceCrossing>();
            Assert.False(HullBlocked(q, new Vector3(6f, 5f, 0.5f), new Vector3(6f, 15f, 0.5f), into), "through the garden wall's gap");
            Assert.True(HullBlocked(q, new Vector3(12f, 5f, 0.5f), new Vector3(12f, 15f, 0.5f), into), "the garden wall beside the gap");
            Assert.True(HullBlocked(q, new Vector3(22f, 5f, 0.5f), new Vector3(22f, 15f, 0.5f), into), "a hole that does not reach the ground");
            Assert.True(HullBlocked(q, new Vector3(25f, 15f, 0.5f), new Vector3(25f, 25f, 0.5f), into), "the building's breach: never for a hull");
            Assert.False(w.SegmentBlocked(new Vector3(25f, 15f, 1.5f), new Vector3(25f, 25f, 1.5f), DoorStates.Of(repo, w)), "sight passes the building's breach");
        }

        /// <summary>⭐ <c>CE-4598</c> (R-2084) — the hull trace and the hull surface every vehicle asks every step allocate nothing once warm.</summary>
        [Fact]
        public void Hull_TheTraceAndTheSurface_AllocateNothing()
        {
            var w = HouseAndGardenWall();
            int garden = GardenPanel(w);
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(garden, 4f, 8f, 0f, 1.0f, BreachKind.Passage));
            var q = new TerrainWorldQuery(w, DoorStates.Of(repo, w));
            var into = new List<Fdp.Toolkit.World.TraceCrossing>(8);
            float sink = 0f;
            void Run()
            {
                for (int x = 2; x < 30; x += 4)
                {
                    q.Trace(new Vector3(x, 5f, 0.5f), new Vector3(x, 15f, 0.5f), Fdp.Toolkit.World.TracePurpose.Hull, into);
                    sink += into.Count + q.HullSurfaceZ(x, 10f, 0f);
                }
            }
            Run(); Run();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Run();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(sink > 0f);
        }


        // ── S-2: the collapse — content, rule, the view's mound, the traces ───────────────────────────────────────

        [Fact]
        public void Content_EveryPieceOfABuilding_NamesItsBuilding_AFreeWallDoesNot()
        {
            var w = BreachFixtures.House();
            Assert.All(w.Prisms, p => Assert.Equal(0, p.Building));
            Assert.All(w.Walkables, p => Assert.Equal(0, p.Building));                                // the roof slab
            Assert.NotEmpty(w.Walkables);
            var withDoor = TerrainWorldParser.Parse(BreachFixtures.DoorHouse, "range");
            Assert.All(withDoor.DoorLeaves, l => Assert.Equal(0, l.Building));
            var free = TerrainWorldParser.Parse("{ \"type\": \"FeatureCollection\", \"features\": [ { \"type\": \"Feature\", \"properties\": { \"kind\": \"wall\", \"height\": 2, \"thickness\": 0.3 }, \"geometry\": { \"type\": \"LineString\", \"coordinates\": [[0,20],[30,20]] } } ] }", "range");
            Assert.All(free.Prisms, p => Assert.Equal(-1, p.Building));
            Assert.Equal(-1, new TerrainPrism().Building);
        }

        /// <summary>🔴 Red-proof: drop the <c>collapseMinKg</c> test in <c>BreachRule.Collapses</c> ⇒ the 155 mm "collapses"; make the distance test <c>&gt;=</c> 0 ⇒ the far burst does.</summary>
        [Fact]
        public void CollapseRule_NeedsTheMassAndAFootprintBurstOrOneNearIt()
        {
            var w = BreachFixtures.House();
            var b = w.Buildings[0];
            var table = BreachTable.Shared;                                                           // collapseMinKg 80, collapseZ 0.5 ⇒ 2.3 m beside the footprint at 94 kg
            Assert.True(BreachRule.Collapses(table, b, new Vector3(25f, 24f, 3f), 94f));                // a Mk-82 on the roof, inside the footprint
            Assert.False(BreachRule.Collapses(table, b, new Vector3(25f, 24f, 3f), 6.6f));              // a 155 mm: far too light
            Assert.False(BreachRule.Collapses(table, b, new Vector3(25f, 24f, 3f), 79f));
            Assert.True(BreachRule.Collapses(table, b, new Vector3(25f, 18f, 1f), 94f));                // 2 m from the south wall: within 0.5 · ∛94 = 2.3 m
            Assert.False(BreachRule.Collapses(table, b, new Vector3(25f, 15f, 1f), 94f));               // 5 m away
            Assert.False(BreachRule.Collapses(table, b, new Vector3(25f, 24f, 3f), 0f));
        }

        /// <summary>
        /// ⭐ A sight line across the building's FORMER interior: blocked by the walls before; after the collapse it is blocked by the mound below ⅓ of the height and passes
        /// above it; fire meets the mound, not a wall. 🔴 Red-proof: make the traces keep skipping nothing (drop the <c>Collapsed(prism.Building)</c> skip) ⇒ the high line
        /// stays blocked; drop the rubble crossing ⇒ the low line passes.
        /// </summary>
        [Fact]
        public void Collapse_TheMoundBlocksBelowAThirdOfTheHeight_AndSightPassesAbove_WhereWallsAndSlabsStoppedItBefore()
        {
            var w = BreachFixtures.House();
            using var repo = BreachFixtures.World(w);
            var low = (From: new Vector3(15f, 24f, 0.5f), To: new Vector3(35f, 24f, 0.5f));
            var high = (From: new Vector3(15f, 24f, 1.6f), To: new Vector3(35f, 24f, 1.6f));
            var down = (From: new Vector3(25f, 24f, 5f), To: new Vector3(25f, 24f, 0.2f));              // down through the roof slab and into the mound
            var fire = new List<TerrainWorld.FireCrossing>();
            var before = DoorStates.Of(repo, w);
            Assert.True(w.SegmentBlocked(low.From, low.To, before));
            Assert.True(w.SegmentBlocked(high.From, high.To, before));
            Assert.True(w.SegmentBlocked(down.From, down.To, before));                               // the roof slab

            BreachFixtures.Collapsed(repo, w, 0);
            var after = DoorStates.Of(repo, w);
            Assert.True(after.Damage!.Collapsed(0));
            var rubble = Assert.Single(after.Damage.Rubble);
            Assert.Equal(TerrainPrismKind.Building, rubble.Kind);
            Assert.Equal(1.0f, rubble.TopZ - rubble.BaseZ, 3);                                       // ⅓ of 3 m
            Assert.Equal("rubble", rubble.Material!.Name);
            Assert.Equal(0, rubble.Building);
            Assert.True(w.SegmentBlocked(low.From, low.To, after));                                  // the mound
            Assert.False(w.SegmentBlocked(high.From, high.To, after));                               // above it: open
            Assert.True(w.SegmentBlocked(down.From, down.To, after));                                // …the roof slab is gone, the mound is not
            Assert.False(w.SegmentBlocked(new Vector3(25f, 24f, 5f), new Vector3(25f, 24f, 1.5f), after));   // down to above the mound: clear
            w.QueryFire(low.From, low.To, fire, after);
            Assert.Contains(fire, c => c.Kind == "prism" && c.Material == "rubble");
            Assert.DoesNotContain(fire, c => c.Kind == "panel");
            w.QueryFire(high.From, high.To, fire, after);
            Assert.Empty(fire);
            Assert.Empty(w.QuerySight(high.From, high.To, after).Crossed);
            Assert.True(w.SegmentBlocked(high.From, high.To, before));                               // a table handed out earlier is untouched
        }

        [Fact]
        public void Collapse_TheDoorsOfACollapsedBuilding_ReadDestroyed_AndTheirLeavesNoLongerBlock()
        {
            var w = TerrainWorldParser.Parse(BreachFixtures.DoorHouse, "range");
            using var repo = new EntityRepository();
            repo.RegisterComponent<DoorState>(); repo.RegisterComponent<BuildingDamage>(); repo.RegisterManagedComponent<TerrainObjectKey>(); repo.SetSingletonManaged(w);
            var door = repo.CreateEntity();
            repo.AddComponent(door, new DoorState { State = TerrainDoorState.Closed });
            repo.SetManagedComponent(door, new TerrainObjectKey { Key = "range/H/front" });
            var thru = (From: new Vector3(25f, 15f, 1.5f), To: new Vector3(25f, 25f, 1.5f));
            var before = DoorStates.Of(repo, w);
            Assert.Equal(TerrainDoorState.Closed, before[0]);
            Assert.True(w.SegmentBlocked(thru.From, thru.To, before));

            BreachFixtures.Collapsed(repo, w, 0);
            var after = DoorStates.Of(repo, w);
            Assert.Equal(TerrainDoorState.Destroyed, after[0]);
            Assert.False(w.SegmentBlocked(thru.From, thru.To, after));
            Assert.Equal(TerrainDoorState.Closed, before[0]);                                         // the held table is untouched
        }

        /// <summary>
        /// ⭐ 0 B in a warm trace through a collapsed building (the mound crossed, the skipped pieces, the rubble list), and the table is the same instance on an unchanged
        /// frame. 🔴 Red-proof: have the rubble loop build a list per call ⇒ the byte count is not 0; rebuild the rubble every call in <c>Of</c> ⇒ <c>Assert.Same</c> fails.
        /// </summary>
        [Fact]
        public void Collapse_HotPath_AWarmTraceThroughACollapsedBuilding_AllocatesNothing_AndTheTableIsTheSameInstance()
        {
            var w = BreachFixtures.House();
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Collapsed(repo, w, 0);
            var fire = new List<TerrainWorld.FireCrossing>(8);
            var lines = new[]
            {
                (new Vector3(15f, 24f, 0.5f), new Vector3(35f, 24f, 0.5f)),   // blocked by the mound
                (new Vector3(15f, 24f, 1.6f), new Vector3(35f, 24f, 1.6f)),   // above it
                (new Vector3(25f, 24f, 5f), new Vector3(25f, 24f, 0.2f)),     // down onto it
            };
            DoorStates doors = DoorStates.Of(repo, w);
            bool blocked = false, passed = false;
            void Run()
            {
                doors = DoorStates.Of(repo, w);
                foreach (var (a, b) in lines)
                {
                    bool x = w.SegmentBlocked(a, b, doors); blocked |= x; passed |= !x;
                    w.QueryFire(a, b, fire, doors);
                }
            }
            Run(); Run();
            var first = doors; var table = doors.Damage;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(blocked && passed);
            Assert.Equal(0L, allocated);
            Assert.Same(first, doors);
            Assert.Same(table, doors.Damage);
            repo.Tick();
            Assert.Same(first, DoorStates.Of(repo, w));                                               // also on a later frame
        }

        // ── recorded by bytes (unmanaged ⇒ the flight recorder keeps it; a checkpoint / replay restores it) ───────

        /// <summary>
        /// 🔴 Red-proof: put a <c>string</c> (a managed field) into <c>BuildingDamage</c> — it no longer compiles as an unmanaged component; or, in the table
        /// build, ignore the restored entity ⇒ the restored view's line is blocked again.
        /// </summary>
        [Fact]
        public void Recorded_TheBreachesOfAKeyframe_AreInTheRestoredWorld()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            using var live = BreachFixtures.World(w);
            BreachFixtures.Damage(live, w, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage));
            using var k0 = new MemoryStream();
            var recorder = new Fdp.Core.FlightRecorder.RecorderSystem();
            using (var bw = new BinaryWriter(k0, System.Text.Encoding.UTF8, leaveOpen: true)) recorder.RecordKeyframe(live, bw, 0L);

            using var restored = BreachFixtures.World(w);
            var playback = new Fdp.Core.FlightRecorder.PlaybackSystem();
            k0.Position = 0;
            using (var br = new BinaryReader(k0)) playback.ApplyFrame(restored, br);

            var damage = DoorStates.Of(restored, w).Damage;
            Assert.NotNull(damage);
            Assert.True(damage!.Opens(south, 5f, 1f));
            Assert.False(w.SegmentBlocked(new Vector3(25f, 15f, 1.5f), new Vector3(25f, 25f, 1.5f), DoorStates.Of(restored, w)));
        }

        /// <summary>
        /// ⭐ <c>CE-4391</c> S-4 (R-4012, AAR seek) — ONE LIVE repository restored B → A → B by the playback keyframe path (<c>PlaybackSystem.ApplyFrame</c>: <c>repo.Clear()</c>, then a raw chunk blit — what a seek does
        /// to each node's own recording): after each restore <see cref="BuildingDamageTable.Of"/>, the door view and a line through the breach match THAT keyframe. A has no damage and a closed door; B has a
        /// passage, a collapse and an open door. The per-thread caches (R-219/R-220) must notice a blit that moves neither the frame stamp nor the add counters.
        /// 🔴 Red-proof: make <c>EntityRepository.Clear</c> not bump <c>StructuralChanges</c> / the add counters (or drop the generation test in the cache) ⇒ the seek back to A keeps serving B's table.
        /// </summary>
        [Fact]
        public void Recorded_ALiveRepositorySeekedBAThenAThenB_ServesEachKeyframesDamageAndDoors()
        {
            var w = TerrainWorldParser.Parse(BreachFixtures.DoorHouse, "range");
            int south = w.Doors[0].Panel;
            static void Reg(EntityRepository r) { r.RegisterComponent<BuildingDamage>(); r.RegisterComponent<DoorState>(); r.RegisterManagedComponent<TerrainObjectKey>(); }
            byte[] Keyframe(bool damaged, bool earlierOther = false)
            {
                using var src = new EntityRepository(); Reg(src); src.SetSingletonManaged(w);
                var door = src.CreateEntity();
                src.AddComponent(door, new DoorState { State = damaged ? TerrainDoorState.Open : TerrainDoorState.Closed });
                src.SetManagedComponent(door, new TerrainObjectKey { Key = w.Doors[0].Key });
                if (earlierOther)   // C: a DIFFERENT breach on the SAME entity slot, recorded at an EARLIER tick than B
                    BreachFixtures.Damage(src, w, 0, new WallBreach(south, 1f, 2f, 1f, 1.5f, BreachKind.Hole));
                else if (damaged)
                {
                    BreachFixtures.Damage(src, w, 0, new WallBreach(south, 7f, 9f, 0f, 2.2f, BreachKind.Passage));
                    BreachFixtures.Collapsed(src, w, 0);
                }
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) new Fdp.Core.FlightRecorder.RecorderSystem().RecordKeyframe(src, bw, earlierOther ? 20L : damaged ? 20L : 10L);
                return ms.ToArray();
            }
            byte[] a = Keyframe(false), b = Keyframe(true), c = Keyframe(true, earlierOther: true);

            using var live = new EntityRepository(); Reg(live); live.SetSingletonManaged(w);
            var playback = new Fdp.Core.FlightRecorder.PlaybackSystem();
            void Seek(byte[] keyframe) { using var br = new BinaryReader(new MemoryStream(keyframe)); playback.ApplyFrame(live, br, processEvents: false); }
            var through = (From: new Vector3(27.5f, 15f, 1.0f), To: new Vector3(27.5f, 25f, 1.0f));   // x 27.5 = along 7.5, through the passage's span in the (otherwise solid) south wall

            void Expect(bool damaged, string at)
            {
                var doors = DoorStates.Of(live, w);
                var table = doors.Damage;
                if (!damaged)
                {
                    Assert.True(table == null || table.Count == 0, at + ": the intact keyframe must show no breach");
                    Assert.True(w.SegmentBlocked(through.From, through.To, doors), at + ": the wall is intact");
                    Assert.True(TerrainDoorState.Closed == doors[0], at);
                }
                else
                {
                    Assert.NotNull(table);
                    Assert.True(table!.Opens(south, 8f, 1f), at);
                    Assert.True(table.Collapsed(0), at);
                    Assert.Equal(1, table.Count);
                    Assert.False(w.SegmentBlocked(through.From, through.To, doors), at + ": the breach lets the line through");
                    Assert.True(TerrainDoorState.Destroyed == doors[0], at);   // a collapsed building's doors read Destroyed
                }
            }
            Seek(b); Expect(true, "B");
            Seek(a); Expect(false, "back to A");
            Seek(b); Expect(true, "forward to B again");
            Seek(b); Expect(true, "B restored over B");
            // C: same entity slots, other breach, an EARLIER tick (the global version goes DOWN): the table must follow the content, not the version
            Seek(c);
            var atC = DoorStates.Of(live, w);
            Assert.Equal(1, atC.Damage!.Count);
            Assert.True(atC.Damage.Opens(south, 1.5f, 1.2f) && !atC.Damage.Opens(south, 8f, 1f), "C: the earlier keyframe's breach, not B's");
            Assert.False(atC.Damage.Collapsed(0));
            Assert.Equal(TerrainDoorState.Open, atC[0]);
            Seek(b); Expect(true, "B after C");
        }
    }
}

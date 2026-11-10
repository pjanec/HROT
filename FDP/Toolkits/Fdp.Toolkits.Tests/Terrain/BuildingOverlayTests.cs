using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-3 (docs/DESIGN_Terrain_Import_UTM.md §8.2 B-10) — the live-damage overlay of <see cref="TerrainObstacles.With(TerrainWorld, IReadOnlyList{TerrainPrism}, BuildingDamageTable?)"/>: a PASSAGE
    /// re-expands its panel with a Gap, a COLLAPSE drops the building's pieces and adds its mound, a HOLE changes nothing — and the base's Panels, Doors and Buildings are never touched.
    /// </summary>
    public sealed class BuildingOverlayTests
    {
        private static readonly TerrainPrism[] None = Array.Empty<TerrainPrism>();

        private static BuildingDamageTable? Table(TerrainWorld w, Action<EntityRepository, int> damage, int south)
        {
            var repo = BreachFixtures.World(w);
            damage(repo, south);
            return DoorStates.Of(repo, w).Damage;
        }

        /// <summary>
        /// A passage in the south wall: the derived world's wall has a gap there — a line the base blocks passes (no door table, no damage view: the GEOMETRY changed) — and the beside line
        /// is still blocked; Panels / Doors / Buildings are the base's own lists. 🔴 Red-proof: skip the re-expansion (drop the <c>ExpandPanelPieces</c> call) ⇒ the line stays blocked.
        /// </summary>
        [Fact]
        public void APassage_OpensAGapInTheDerivedWorld_AndTheBasesListsAreUntouched()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var table = Table(w, (r, s) => BreachFixtures.Damage(r, w, 0, new WallBreach(s, 4f, 6f, 0f, 2.2f, BreachKind.Passage)), south)!;
            Assert.NotEqual(0UL, table.NavHash);

            var derived = TerrainObstacles.With(w, None, table);
            Assert.NotSame(w, derived);
            Assert.Same(w.Panels, derived.Panels); Assert.Same(w.Doors, derived.Doors); Assert.Same(w.Buildings, derived.Buildings);   // ⛔ never removed or reordered
            var through = (From: new Vector3(25f, 15f, 1.0f), To: new Vector3(25f, 25f, 1.0f));
            var beside = (From: new Vector3(22f, 15f, 1.0f), To: new Vector3(22f, 25f, 1.0f));
            Assert.True(w.SegmentBlocked(through.From, through.To));
            Assert.False(derived.SegmentBlocked(through.From, through.To));
            Assert.True(derived.SegmentBlocked(beside.From, beside.To));
            Assert.True(derived.SegmentBlocked(new Vector3(25f, 15f, 2.6f), new Vector3(25f, 25f, 2.6f)));   // the wall over the passage's head (2.2 m)
            Assert.Contains(derived.Prisms, p => p.Panel == south && p.BaseZ >= 2.2f - 1e-3f);                // the lintel strip
            Assert.All(derived.Prisms.Where(p => p.Panel == south), p => Assert.Equal(0, p.Building));
        }

        /// <summary>A passage overlapping a door opening merges into ONE wider gap (the expander needs ascending, non-overlapping openings).</summary>
        [Fact]
        public void APassageOverADoor_MergesIntoOneGap()
        {
            var w = TerrainWorldParser.Parse(BreachFixtures.DoorHouse, "range");   // the door spans along 4.5 .. 5.5
            using var repo = new EntityRepository();
            repo.RegisterComponent<BuildingDamage>(); repo.RegisterManagedComponent<TerrainObjectKey>(); repo.RegisterComponent<DoorState>(); repo.SetSingletonManaged(w);
            BreachFixtures.Damage(repo, w, 0, new WallBreach(w.Doors[0].Panel, 5f, 7f, 0f, 2.4f, BreachKind.Passage));
            var derived = TerrainObstacles.With(w, None, DoorStates.Of(repo, w).Damage!);
            // along 4.5 .. 7: one gap, floor to 2.4 (the door's head is 2.1): nothing of the wall stands under 2.4 m there
            Assert.False(derived.SegmentBlocked(new Vector3(26.5f, 15f, 1f), new Vector3(26.5f, 25f, 1f)));
            // (the door's own LEAF still stands across its opening — doors' leaves are unaffected, B-8; the navmesh bakes doors open and never sees it)
            Assert.True(derived.SegmentBlocked(new Vector3(26.5f, 15f, 2.6f), new Vector3(26.5f, 25f, 2.6f)));
            Assert.Equal(w.Doors[0].Panel, derived.Doors[0].Panel);   // the door still names its panel and opening
        }

        /// <summary>A HOLE (and nothing else) changes no walkable: no overlay, the base world itself comes back — and its NavHash is 0 however many there are. 🔴 Red-proof: hash holes too ⇒ NavHash ≠ 0.</summary>
        [Fact]
        public void AHole_ChangesNothing_NoOverlay_NoNavHash()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var table = Table(w, (r, s) =>
            {
                for (int i = 0; i < 10; i++) BreachFixtures.Damage(r, w, 0, new WallBreach(s, i * 0.9f, i * 0.9f + 0.5f, 1f, 1.5f, BreachKind.Hole));
            }, south)!;
            Assert.Equal(10, table.Count);
            Assert.Equal(0UL, table.NavHash);
            Assert.Same(w, TerrainObstacles.With(w, None, table));
        }

        /// <summary>The hash is stable for equal damage and moves with a passage; two tables built from the same entities in any order agree.</summary>
        [Fact]
        public void NavHash_IsStable_AndMovesWithAPassage()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var a = Table(w, (r, s) => { BreachFixtures.Damage(r, w, 0, new WallBreach(s, 4f, 6f, 0f, 2.2f, BreachKind.Passage)); BreachFixtures.Damage(r, w, 0, new WallBreach(s, 8f, 9f, 0f, 2f, BreachKind.Passage)); }, south)!;
            var b = Table(w, (r, s) => { BreachFixtures.Damage(r, w, 0, new WallBreach(s, 8f, 9f, 0f, 2f, BreachKind.Passage)); BreachFixtures.Damage(r, w, 0, new WallBreach(s, 4f, 6f, 0f, 2.2f, BreachKind.Passage)); }, south)!;
            var c = Table(w, (r, s) => BreachFixtures.Damage(r, w, 0, new WallBreach(s, 4f, 6f, 0f, 2.2f, BreachKind.Passage)), south)!;
            Assert.Equal(a.NavHash, b.NavHash);
            Assert.NotEqual(a.NavHash, c.NavHash);
        }

        /// <summary>
        /// A COLLAPSE: the building's panel prisms and its roof slab are gone from the derived world, its mound is there, <c>SurfacesAt</c> no longer offers the roof, and the damage view of
        /// the DERIVED world adds no second mound. 🔴 Red-proof: keep the walkables ⇒ the roof level stays in <c>SurfacesAt</c>; drop the <c>BakedCollapsed</c> exclusion ⇒ the view holds a second mound.
        /// </summary>
        [Fact]
        public void ACollapse_DropsTheBuildingsPiecesAndAddsItsMound_SoSurfaceQueriesAnswerForTheDamagedWorld()
        {
            var w = BreachFixtures.House();
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Collapsed(repo, w, 0);
            var table = DoorStates.Of(repo, w).Damage!;
            Assert.Contains(3f, w.SurfacesAt(25f, 24f, out _));                                       // the roof, before

            var derived = TerrainObstacles.With(w, None, table);
            Assert.DoesNotContain(derived.Prisms, p => p.Panel >= 0);                                 // no wall piece left
            Assert.Empty(derived.Walkables);                                                          // the roof slab
            var mound = Assert.Single(derived.Prisms);
            Assert.Equal(TerrainPrismKind.Building, mound.Kind);
            Assert.Equal(1.0f, mound.TopZ - mound.BaseZ, 3);
            Assert.Equal(new[] { 0 }, derived.BakedCollapsed.ToArray());
            Assert.Same(w.Buildings, derived.Buildings);
            Assert.DoesNotContain(3f, derived.SurfacesAt(25f, 24f, out _));                           // SurfaceZ / SurfacesAt answer right after the bake
            Assert.Equal(1.0f, derived.SurfaceZ(25f, 24f, 1.0f), 3);                                   // standing on the mound

            using var view = BreachFixtures.World(derived);
            BreachFixtures.Collapsed(view, derived, 0);
            Assert.Empty(DoorStates.Of(view, derived).Damage!.Rubble);                                // its mound is in the geometry: not crossed twice
            Assert.True(derived.SegmentBlocked(new Vector3(15f, 24f, 0.5f), new Vector3(35f, 24f, 0.5f), DoorStates.Of(view, derived)));   // the baked mound blocks
            Assert.False(derived.SegmentBlocked(new Vector3(15f, 24f, 1.6f), new Vector3(35f, 24f, 1.6f), DoorStates.Of(view, derived)));
        }

        /// <summary>An obstacle and the overlay compose: obstacles join the end of the list as before.</summary>
        [Fact]
        public void ObstaclesAndOverlay_Compose()
        {
            var w = BreachFixtures.House();
            using var repo = BreachFixtures.World(w);
            BreachFixtures.Collapsed(repo, w, 0);
            var car = TerrainObstacles.PrismOf(new Vector3(40f, 5f, 0f), 0f, new ObstacleShape { Length = 4f, Width = 2f, Height = 1.5f }, w.Materials.Get("concrete", "t"), "obstacle:1");
            var derived = TerrainObstacles.With(w, new[] { car }, DoorStates.Of(repo, w).Damage);
            Assert.Equal(2, derived.Prisms.Count);
            Assert.Same(car, derived.Prisms[^1]);
        }
    }
}

using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using FDP.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2236</c> (CV-7; docs/DESIGN_Civilians_And_Crowd_Control.md §5e) — the shelter query's own pieces on the SHIPPED bt-range:
    /// a candidate per exterior door that is not locked, standing inside the house; the inner door and a locked door give none; and
    /// the indoor test agrees with the footprints.
    /// </summary>
    public class EnterableShelterTests
    {
        private static TerrainWorld BtRange()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors"))) dir = dir.Parent;
            var folder = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range");
            return TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
        }

        private static TerrainDoorDef Door(TerrainWorld w, string key) => w.Doors[w.DoorIndexOf(key)];

        [Fact]
        public void CE2236_AnExteriorDoorGivesAPointInsideItsHouse_TheInnerDoorNone()
        {
            var w = BtRange();
            var houseA = w.Buildings.Single(b => b.Label == "House A");
            Assert.False(houseA.Solid);
            foreach (var key in new[] { "bt-range/House A/front", "bt-range/House A/back" })
            {
                Assert.True(EnterableDoorGenerator.TryInside(w, Door(w, key), 2f, out var inside), key);
                Assert.True(PolygonMath.Contains(houseA.Footprint, inside), $"{key}: {inside} inside House A");
                Assert.True(Vector2.Distance(inside, Door(w, key).Center) <= 2.01f);
            }
            Assert.False(EnterableDoorGenerator.TryInside(w, Door(w, "bt-range/House A/hall"), 2f, out _), "the hall door is inside the house: no way IN");
        }

        [Fact]
        public void CE2236_TheGenerator_SkipsALockedDoor_AndEveryCandidateIsIndoors()
        {
            var w = BtRange();
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.SetSingletonManaged(w);
            var self = repo.CreateEntity();
            repo.AddComponent(self, new SimTransform { Position = new Vector3(105, 90, 0), Rotation = Quaternion.Identity });
            var sensor = new EqsSensor { SearchRadius = 60f, ContextSlot0 = self };
            Span<EqsResult> results = stackalloc EqsResult[32];
            int n = new EnterableDoorGenerator().Generate(self, ref sensor, repo, results);

            Assert.True(n >= 2, $"House A's back door and House B's doors (bt-range authors A's front Locked): {n} candidates");
            var lockedFront = Door(w, "bt-range/House A/front").Center;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector2(results[i].PositionX, results[i].PositionY);
                Assert.True(InsideTest.IsInside(w, p, out _), $"candidate {p} is indoors");
                Assert.True(Vector2.Distance(p, lockedFront) > 2.5f, $"candidate {p} is not through A's LOCKED front door");
            }
            var houseA = w.Buildings.Single(b => b.Label == "House A");
            bool intoA = false;
            for (int i = 0; i < n; i++) intoA |= PolygonMath.Contains(houseA.Footprint, new Vector2(results[i].PositionX, results[i].PositionY));
            Assert.True(intoA, "a candidate inside House A — through its back door");
        }

        [Fact]
        public void CE2236_TheIndoorTest_KnowsInFromOut_AndTheTemplateIdIsTheCanonicalOne()
        {
            var w = BtRange();
            Assert.True(InsideTest.IsInside(w, new Vector2(105, 104), out int b) && w.Buildings[b].Label == "House A");
            Assert.False(InsideTest.IsInside(w, new Vector2(105, 90), out _));
            Assert.Equal(EqsTemplateRegistry.BlueprintIdOf(Guid.Parse(FindEnterableShelter.AssetId)), FindEnterableShelter.BlueprintId);
        }
    }
}

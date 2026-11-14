using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ T-8 (CE-4424, docs/DESIGN_Terrain_Authoring.md §18j) — <see cref="TerrainBake.CheckOverwrite"/> / <see cref="TerrainChangeCheck"/>: what re-saving a terrain would do to the scenarios that use it. The terrain <c>saved-t</c> is saved once from a source scenario;
    /// a USER scenario (header terrain <c>saved-t</c>) holds a squad, a route, an area and a saved door state; then a CHANGED source is checked against the saved terrain. Nothing is written to the target.
    /// </summary>
    public sealed class TerrainChangeCheckTests : IDisposable
    {
        private const string G1 = "b1000000-0000-4000-8000-000000000001", G2 = "b1000000-0000-4000-8000-000000000002", G3 = "b1000000-0000-4000-8000-000000000003";
        private readonly OverlayFixture _f = new();
        private string Target => Path.Combine(_f.Assets, "terrain", "saved-t");
        private string Users => Path.Combine(_f.Root, "users");

        public void Dispose() => _f.Dispose();

        private void WriteScenario(string dir, JsonObject dom) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "scenario.json"), dom.ToJsonString()); }

        private string Source(params (string Guid, JsonObject Entity)[] entities)
        {
            string dir = Path.Combine(_f.Root, "src-" + Guid.NewGuid().ToString("N")[..6]);
            WriteScenario(dir, _f.Scenario(entities));
            return dir;
        }

        /// <summary>Saves <paramref name="entities"/> as the terrain <c>saved-t</c> (the OLD terrain of the overwrite).</summary>
        private void SaveTerrain(params (string Guid, JsonObject Entity)[] entities)
            => TerrainBake.SaveAs(Source(entities), Target, "saved-t", _f.Resolve, new[] { _f.Recipes }, bakeNavtiles: false);

        private static JsonObject Unit(float x, float y, string name) => new() { ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(x, y, 0) }, ["EntityInfo"] = new JsonObject { ["Name"] = name } };

        private static JsonObject Route(float x, float y, string name, params (float X, float Y)[] wps)
        {
            var u = Unit(x, y, name);
            var arr = new JsonArray(); foreach (var (wx, wy) in wps) arr.Add(new JsonObject { ["X"] = wx, ["Y"] = wy, ["Z"] = 0 });
            u["RoutePlan"] = new JsonObject { ["Waypoints"] = arr, ["IsLoop"] = false };
            return u;
        }

        private static JsonObject Area(float x, float y, string name, params (float X, float Y)[] pts)
        {
            var u = Unit(x, y, name);
            var arr = new JsonArray(); foreach (var (px, py) in pts) arr.Add(new JsonObject { ["X"] = px, ["Y"] = py });
            u["EditablePolyline"] = new JsonObject { ["Points"] = arr, ["Version"] = 0 };
            return u;
        }

        private void User(params (string Guid, JsonObject Entity)[] entities) => User(null, entities);

        private void User(JsonObject? terrainObjects, params (string Guid, JsonObject Entity)[] entities)
        {
            var e = new JsonObject(); foreach (var (g, o) in entities) e[g] = o;
            var dom = new JsonObject { ["header"] = new JsonObject { ["terrainName"] = "saved-t" }, ["entities"] = e };
            if (terrainObjects != null) dom["TerrainObjects"] = terrainObjects;
            WriteScenario(Path.Combine(Users, "u1"), dom);
        }

        private System.Collections.Generic.IReadOnlyList<TerrainFinding> Check(string source)
            => TerrainBake.CheckOverwrite(source, Target, "saved-t", _f.Resolve, new[] { Users }, new[] { _f.Recipes });

        private static string Hash(string folder) => string.Join("|", Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.GetRelativePath(folder, p) + ":" + new FileInfo(p).Length));

        /// <summary>⭐ a squad a NEW house would stand on is listed (and a house MOVED onto it); 🔴 Red-proof: skip the <c>Inside</c> test in <c>Check</c> ⇒ nothing listed ⇒ red.</summary>
        [Fact]
        public void T8_ASquadANewHouseWouldStandOn_IsListed_AndAMovedHouseToo()
        {
            SaveTerrain((G1, OverlayFixture.House("hut", 300, 300)));
            User(("c0000000-0000-4000-8000-000000000001", Unit(205, 204, "Alpha")), ("c0000000-0000-4000-8000-000000000002", Unit(45, 344, "Bravo")));
            string before = Hash(Target);

            var added = Check(Source((G1, OverlayFixture.House("hut", 300, 300)), (G2, OverlayFixture.House("hut", 200, 200))));
            var f = Assert.Single(added, x => x.Kind == TerrainFindingKind.StandsIn);
            Assert.Equal("Alpha", f.EntityName); Assert.StartsWith("b:", f.PartKey);
            Assert.DoesNotContain(added, x => x.EntityName == "Bravo");
            Assert.Equal(before, Hash(Target));   // nothing of the target was written

            var moved = Check(Source((G1, OverlayFixture.House("hut", 40, 340))));   // the house G1 moves onto Bravo
            Assert.Contains(moved, x => x.Kind == TerrainFindingKind.StandsIn && x.EntityName == "Bravo");
        }

        /// <summary>⭐ a route leg a new HEDGE cuts is listed (the hedge is a wall buffered by its thickness); 🔴 Red-proof: skip <c>SegmentHits</c> ⇒ nothing ⇒ red.</summary>
        [Fact]
        public void T8_ARouteLegANewHedgeCuts_IsListed()
        {
            SaveTerrain((G1, OverlayFixture.House("hut", 300, 300)));
            User(("c0000000-0000-4000-8000-000000000003", Route(100, 250, "Convoy", (300, 250))), ("c0000000-0000-4000-8000-000000000004", Route(100, 20, "Safe", (300, 20))));
            var found = Check(Source((G1, OverlayFixture.House("hut", 300, 300)), (G3, OverlayFixture.Feature(4, 200, 230, points: new[] { (0f, 0f), (0f, 40f) }))));
            var f = Assert.Single(found, x => x.Kind == TerrainFindingKind.RouteCrosses);
            Assert.Equal("Convoy", f.EntityName); Assert.StartsWith("w:", f.PartKey); Assert.NotNull(f.To);
        }

        /// <summary>⭐ a door state whose house was REMOVED is listed — by the section key and by a behaviour parameter shaped like it; a door whose house stays is not. 🔴 Red-proof: invert <c>Resolves(newWorld, …)</c> ⇒ red.</summary>
        [Fact]
        public void T8_ADoorStateWhoseHouseWasRemoved_IsListed()
        {
            SaveTerrain((G1, OverlayFixture.House("hut", 300, 300)), (G2, OverlayFixture.House("hut", 150, 300)));
            var objects = new JsonObject { [$"saved-t/{G1}/0/front"] = new JsonObject { ["door"] = "Locked" }, [$"saved-t/{G2}/0/front"] = new JsonObject { ["door"] = "Open" } };
            var guard = Unit(10, 10, "Guard"); guard["Params"] = new JsonObject { ["Door"] = $"saved-t/{G2}/0/front" };
            User(objects, ("c0000000-0000-4000-8000-000000000005", guard));

            var found = Check(Source((G1, OverlayFixture.House("hut", 300, 300))));   // G2's house is gone
            Assert.Equal(2, found.Count(x => x.Kind == TerrainFindingKind.KeyLost));
            Assert.All(found.Where(x => x.Kind == TerrainFindingKind.KeyLost), x => Assert.Contains(G2, x.PartKey));
            Assert.Contains(found, x => x.EntityName == "Guard"); Assert.Contains(found, x => x.EntityName == "TerrainObjects");
        }

        /// <summary>⭐ an area over a MOVED wood is listed (informational); an area elsewhere is not. 🔴 Red-proof: skip <c>Overlap</c> ⇒ red.</summary>
        [Fact]
        public void T8_AnAreaOverAMovedWood_IsListed()
        {
            var wood = OverlayFixture.Feature(5, 300, 100, points: new[] { (0f, 0f), (20f, 0f), (20f, 20f), (0f, 20f) });
            var woodMoved = OverlayFixture.Feature(5, 330, 100, points: new[] { (0f, 0f), (20f, 0f), (20f, 20f), (0f, 20f) });
            SaveTerrain((G3, wood));
            User(("c0000000-0000-4000-8000-000000000006", Area(295, 95, "Zone over", (0, 0), (30, 0), (30, 30), (0, 30))), ("c0000000-0000-4000-8000-000000000007", Area(20, 20, "Zone far", (0, 0), (10, 0), (10, 10), (0, 10))));
            var found = Check(Source((G3, woodMoved)));
            Assert.Contains(found, x => x.Kind == TerrainFindingKind.AreaOverlaps && x.EntityName == "Zone over");
            Assert.DoesNotContain(found, x => x.EntityName == "Zone far");
        }

        /// <summary>⭐ an UNCHANGED re-save lists nothing — even with a squad inside a house, a route through the block and a saved door; and a part nudged by 5 mm (under 1 cm) is not moved.</summary>
        [Fact]
        public void T8_AnUnchangedResave_ListsNothing_AndAFiveMillimetreNudgeIsNotAMove()
        {
            SaveTerrain((G1, OverlayFixture.House("hut", 300, 300)));
            var objects = new JsonObject { [$"saved-t/{G1}/0/front"] = new JsonObject { ["door"] = "Locked" } };
            User(objects, ("c0000000-0000-4000-8000-000000000008", Unit(305, 304, "Inside")), ("c0000000-0000-4000-8000-000000000009", Route(250, 250, "Past", (350, 350))),
                 ("c0000000-0000-4000-8000-00000000000a", Area(290, 290, "Over", (0, 0), (30, 0), (30, 30), (0, 30))));
            Assert.Empty(Check(Source((G1, OverlayFixture.House("hut", 300, 300)))));
            Assert.Empty(Check(Source((G1, OverlayFixture.House("hut", 300.005f, 300)))));
            Assert.NotEmpty(Check(Source((G1, OverlayFixture.House("hut", 300.05f, 300)))));   // 5 cm IS a move: the inside squad stands in the moved house, the area overlaps it
        }
    }
}

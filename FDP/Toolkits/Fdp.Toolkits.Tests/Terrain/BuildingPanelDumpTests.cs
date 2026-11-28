using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-11052</c> (the tool <c>hrot-building-panels</c>; 📄 docs/designs/IG/DRAFT_Unreal_Image_Generator.md "Building panels for Unreal") — a terrain's panels and openings as JSON,
    /// IN THE ORDER HROT's own damage code indexes them. The judge is HROT's own code: a breach and a window hit are written through the damage records and the SAVE
    /// (<see cref="TerrainObjectsSection"/>, the same <see cref="BuildingPanelIndex"/> rank the wire uses), and the dump must show, at the (key, panel, opening) the save wrote,
    /// the very wall and opening the damage was applied to.
    /// </summary>
    public sealed class BuildingPanelDumpTests
    {
        // two houses: "A" (one storey, no openings) and "B" (two storeys; a door in the south wall of the ground storey, a window in the east wall of the upper one) — B's panels have
        // global indices past A's, so a within-building rank is NOT the global index (the case a mix-up hides in).
        private const string Houses = """
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "building", "label": "A",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                      { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } },
              { "type": "Feature", "properties": { "kind": "building", "label": "B", "doors": { "front": "closed" },
                  "building": { "footprint": [[0,0],[12,0],[12,9],[0,9]],
                    "storeys": [
                      { "height": 3, "walls": [
                        { "from": [0,0], "to": [12,0], "thickness": 0.3, "material": "brick", "openings": [ { "kind": "door", "at": 4.5, "width": 1, "doorId": "front" } ] },
                        { "from": [12,0], "to": [12,9], "thickness": 0.3, "material": "brick" },
                        { "from": [12,9], "to": [0,9], "thickness": 0.3, "material": "brick" },
                        { "from": [0,9], "to": [0,0], "thickness": 0.3, "material": "brick" } ] },
                      { "height": 3, "walls": [
                        { "from": [0,0], "to": [12,0], "thickness": 0.3, "material": "brick" },
                        { "from": [12,0], "to": [12,9], "thickness": 0.3, "material": "brick", "openings": [ { "kind": "window", "at": 3, "width": 1.4 } ] },
                        { "from": [12,9], "to": [0,9], "thickness": 0.3, "material": "brick" },
                        { "from": [0,9], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [60, 40] } } ] }
            """;

        private static TerrainWorld World() => TerrainWorldParser.Parse(Houses, "range");

        private static JsonObject Dump(TerrainWorld w) => (JsonObject)JsonNode.Parse(BuildingPanelDump.ToJson(w))!;

        private static JsonObject Building(JsonObject dump, string key)
            => (JsonObject)dump["buildings"]!.AsArray().Single(b => (string)b!["key"]! == key)!;

        private static float F(JsonNode? n) => (float)n!;

        /// <summary>
        /// ⭐⭐ THE invariant. A breach on a panel of house B and a window hit on B's upper-storey window are applied through HROT's damage code and written through the save
        /// (the panel as its rank WITHIN the building — the wire's <c>BuildingDamage.Panel</c> / <c>WindowDamage.Panel</c>). At that (key, panel, opening) the dump shows the SAME wall (both
        /// ends, base, top, storey) and the SAME opening (kind, along, width, sill, head) the damage was applied to.
        /// 🔴 Red-proof: shuffle the panel order in <see cref="BuildingPanelDump"/> (or number the panels by global index) ⇒ the ends no longer match.
        /// </summary>
        [Fact]
        public void ABreachAndAWindowHit_AppliedThroughHrotsCode_AreFoundAtTheSamePanelAndOpeningInTheDump()
        {
            var w = World();
            int b = w.Buildings.ToList().FindIndex(x => x.Label == "B");
            // B's window: the panel of the upper storey with an opening of kind window; B's breach: its ground-storey east wall
            int windowPanel = Enumerable.Range(0, w.Panels.Count).Single(i => w.Panels[i].Building == b && w.Panels[i].Openings.Any(o => o.Kind == TerrainOpeningKind.Window));
            int windowOpening = w.Panels[windowPanel].Openings.ToList().FindIndex(o => o.Kind == TerrainOpeningKind.Window);
            int breachPanel = Enumerable.Range(0, w.Panels.Count).First(i => w.Panels[i].Building == b && w.Panels[i].Storey == 0 && w.Panels[i].A.X > 11f && w.Panels[i].B.X > 11f);
            Assert.NotEqual(BuildingPanelIndex.ToLocal(w, breachPanel), breachPanel);   // the fixture can tell a rank from a global index

            using var repo = new EntityRepository();
            repo.RegisterComponent<BuildingDamage>();
            repo.RegisterComponent<WindowDamage>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            repo.SetSingletonManaged(w);
            var bd = new BuildingDamage { Building = b };
            BuildingDamages.Add(ref bd, new WallBreach(breachPanel, 2f, 3.5f, 0.2f, 2.4f, BreachKind.Hole), null);
            var wd = new WindowDamage { Building = b };
            Assert.Equal(WindowChange.Broke, WindowDamages.Break(ref wd, windowPanel, windowOpening));
            var e = repo.CreateEntity();
            repo.AddComponent(e, bd);
            repo.AddComponent(e, wd);
            repo.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(w, b) });

            var section = TerrainObjectsSection.Write(repo)!;
            var key = Assert.Single(section).Key;
            var entry = (JsonObject)section[key]!;
            int savedBreachPanel = (int)entry["breaches"]![0]![0]!;
            var savedWindow = entry["windows"]![0]!.AsArray();
            int savedWindowPanel = (int)savedWindow[0]!, savedWindowOpening = (int)savedWindow[1]!;

            var dump = Dump(w);
            var building = Building(dump, key);
            var panels = building["panels"]!.AsArray();

            var wb = w.Panels[breachPanel];
            var pb = (JsonObject)panels[savedBreachPanel]!;
            Assert.Equal(savedBreachPanel, (int)pb["panel"]!);
            Assert.Equal(wb.A.X, F(pb["a"]![0])); Assert.Equal(wb.A.Y, F(pb["a"]![1]));
            Assert.Equal(wb.B.X, F(pb["b"]![0])); Assert.Equal(wb.B.Y, F(pb["b"]![1]));
            Assert.Equal(wb.BaseZ, F(pb["base"])); Assert.Equal(wb.TopZ, F(pb["top"])); Assert.Equal(wb.Storey, (int)pb["storey"]!);

            var ww = w.Panels[windowPanel];
            var pw = (JsonObject)panels[savedWindowPanel]!;
            Assert.Equal(ww.A.X, F(pw["a"]![0])); Assert.Equal(ww.A.Y, F(pw["a"]![1]));
            Assert.Equal(ww.B.X, F(pw["b"]![0])); Assert.Equal(ww.B.Y, F(pw["b"]![1]));
            Assert.Equal(ww.Storey, (int)pw["storey"]!);
            var ow = ww.Openings[windowOpening];
            var ojson = (JsonObject)pw["openings"]![savedWindowOpening]!;
            Assert.Equal(savedWindowOpening, (int)ojson["opening"]!);
            Assert.Equal("window", (string)ojson["kind"]!);
            Assert.Equal(ow.At, F(ojson["at"])); Assert.Equal(ow.Width, F(ojson["width"]));
            Assert.Equal(ow.SillZ, F(ojson["sill"])); Assert.Equal(ow.HeadZ, F(ojson["head"]));
            Assert.Equal(ow.Glazed, (bool)ojson["glazed"]!);
        }

        /// <summary>
        /// ⭐ Every panel of the world sits at <c>BuildingPanelIndex.ToLocal</c> in its group's list, with its openings in list order — and the dump holds every group (buildings, then
        /// free-wall lines) under <see cref="BuildingDamages.KeyOf"/>, each panel once. The door carries its id.
        /// 🔴 Red-proof: iterate <c>world.Panels</c> and number by position ⇒ B's ranks restart at the wrong place; drop the <c>doorId</c> write ⇒ the door row.
        /// </summary>
        [Fact]
        public void EveryPanel_IsAtItsRank_InItsGroupsKey_WithItsOpeningsInOrder()
        {
            var w = World();
            var dump = Dump(w);
            var groups = dump["buildings"]!.AsArray();
            Assert.Equal(WallGroups.Count(w), groups.Count);
            Assert.Equal(w.Panels.Count, groups.Sum(g => g!["panels"]!.AsArray().Count));
            for (int gp = 0; gp < w.Panels.Count; gp++)
            {
                int g = WallGroups.Of(w, gp), rank = BuildingPanelIndex.ToLocal(w, gp);
                var group = (JsonObject)groups[g]!;
                Assert.Equal(BuildingDamages.KeyOf(w, g), (string)group["key"]!);
                var p = (JsonObject)group["panels"]![rank]!;
                var panel = w.Panels[gp];
                Assert.Equal(panel.A.X, F(p["a"]![0]));
                Assert.Equal(panel.B.Y, F(p["b"]![1]));
                var openings = p["openings"]!.AsArray();
                Assert.Equal(panel.Openings.Count, openings.Count);
                for (int o = 0; o < openings.Count; o++)
                {
                    Assert.Equal(panel.Openings[o].At, F(openings[o]!["at"]));
                    Assert.Equal(panel.Openings[o].Kind == TerrainOpeningKind.Door ? "door" : panel.Openings[o].Kind == TerrainOpeningKind.Window ? "window" : "gap", (string)openings[o]!["kind"]!);
                }
            }
            var door = Building(dump, "range/B")["panels"]!.AsArray().SelectMany(p => p!["openings"]!.AsArray()).Single(o => (string)o!["kind"]! == "door");
            Assert.Equal("front", (string)door!["doorId"]!);
            Assert.Equal("range/B/front", (string)door["doorKey"]!);
        }

        /// <summary>⭐ Deterministic and language-neutral (user R-4221): the same world writes the same bytes twice — also under <c>cs-CZ</c>, where a culture-aware writer would print decimal commas.
        /// 🔴 Red-proof: write the numbers with <c>ToString()</c> and no invariant culture ⇒ the cs-CZ bytes differ.</summary>
        [Fact]
        public void TheDump_IsByteIdenticalTwice_AndUnderCzechCulture_WithNoDecimalComma()
        {
            var w = World();
            var a = BuildingPanelDump.ToJson(w);
            var b = BuildingPanelDump.ToJson(w);
            Assert.Equal(a, b);
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
                var c = BuildingPanelDump.ToJson(World());
                Assert.Equal(a, c);
            }
            finally { CultureInfo.CurrentCulture = old; }
            var text = Encoding.UTF8.GetString(a);
            Assert.DoesNotMatch(@"\d,\d", text.Replace(",\r\n", "").Replace(",\n", ""));   // a comma between two digits would be a decimal comma (array commas end a line or are followed by a space)
        }

        /// <summary>⭐ A real committed terrain through the tool's own loader step (<see cref="TerrainFolderWorld"/>): the dump is deterministic and holds every panel of the world, in rank order, keyed by its terrain.
        /// Skips when the repository's <c>assets/terrain/cqb-village</c> is not reachable from the test's folder.</summary>
        [Fact]
        public void ARealTerrain_cqbVillage_DumpsEveryPanel_Deterministically()
        {
            string? dir = AppContext.BaseDirectory;
            string? def = null;
            while (dir != null && def == null)
            {
                string cand = Path.Combine(dir, "assets", "terrain", "cqb-village", "terrain.json");
                if (File.Exists(cand)) def = cand;
                dir = Path.GetDirectoryName(dir);
            }
            if (def == null) return;
            var definition = TerrainFolderWorld.ReadDefinition(def);
            var world = TerrainFolderWorld.ReadWorld(def, definition, definition.Name ?? "cqb-village")!;
            Assert.True(world.Panels.Count > 0);
            var a = BuildingPanelDump.ToJson(world);
            Assert.Equal(a, BuildingPanelDump.ToJson(world));
            var groups = ((JsonObject)JsonNode.Parse(a)!)["buildings"]!.AsArray();
            Assert.Equal(world.Panels.Count, groups.Sum(g => g!["panels"]!.AsArray().Count));
            Assert.Equal(world.Buildings.Count, groups.Count(g => (string)g!["kind"]! == "building"));
            foreach (var g in groups)
            {
                var panels = g!["panels"]!.AsArray();
                for (int i = 0; i < panels.Count; i++) Assert.Equal(i, (int)panels[i]!["panel"]!);
            }
        }
    }
}

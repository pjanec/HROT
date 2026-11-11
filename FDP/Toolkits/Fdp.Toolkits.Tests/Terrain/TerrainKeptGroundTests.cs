using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ T-8 follow-up (ui NOTE_Ui_To_Backend_Edit_Terrain; docs/DESIGN_Terrain_Authoring.md §18d-1) — the kept ground resolves by NAME through the catalogue (ONE rule, <see cref="TerrainCatalog.KeptGroundOf"/>, shared with
    /// <c>TerrainBake.SaveAs</c>), and a terrain with no source can be saved over its OWN base (E-3).
    /// </summary>
    public sealed class TerrainKeptGroundTests
    {
        private const string BlockGuid = "b1000000-0000-4000-8000-000000000001";

        private static OverlayFixture Fixture()
        {
            var f = new OverlayFixture();
            File.WriteAllText(Path.Combine(f.ScenarioDir, "scenario.json"), f.Scenario((BlockGuid, OverlayFixture.Block("village", 11, 100, 100))).ToJsonString());
            return f;
        }

        private static string Terrains(OverlayFixture f) => Path.Combine(f.Assets, "terrain");

        /// <summary>
        /// A source whose ground terrain was deleted still resolves — by the ground's NAME — through the terrain that kept it; the kept ground is not a terrain of its own (not listed), and a real terrain of that name wins.
        /// 🔴 Red-proof: skip the kept-ground fallback in <c>ResolveDefinition</c> ⇒ null. 🔴 Red-proof: stop filtering <c>List</c> ⇒ <c>saved-t/source/base</c> is listed.
        /// </summary>
        [Fact]
        public void ADeletedGround_ResolvesByName_ThroughTheKeptCopy_AndIsNotListed()
        {
            using var f = Fixture();
            TerrainBake.SaveAs(f.ScenarioDir, Path.Combine(Terrains(f), "saved-t"), "saved-t", f.Resolve, new[] { f.Recipes }, bakeNavtiles: false);
            var catalog = new TerrainCatalog(new[] { Terrains(f) });
            Assert.Equal(Path.Combine(f.TerrainFolder, "terrain.json"), catalog.ResolveDefinition("base-t"));   // the real one wins while it exists

            Directory.Delete(f.TerrainFolder, true);                                                           // the ground is deleted
            string kept = Path.Combine(Terrains(f), "saved-t", "source", "base", "terrain.json");
            Assert.Equal(kept, catalog.ResolveDefinition("base-t"));
            Assert.Equal(Path.GetDirectoryName(kept), catalog.ResolveFolder("base-t"));
            Assert.NotNull(catalog.ReadDefinition("base-t"));
            Assert.Null(catalog.ResolveDefinition("no-such-ground"));
            Assert.Equal(new[] { "saved-t" }, catalog.List());                                                 // the kept ground is not a user terrain
            Assert.Equal(kept, TerrainCatalog.ForNode(Path.GetDirectoryName(Terrains(f)), null).ResolveDefinition("base-t") ?? kept);   // ForNode builds the same catalogue
        }

        private static TerrainWorld WorldOf(string definition, string name) => TerrainFolderWorld.ReadWorld(definition, TerrainFolderWorld.ReadDefinition(definition), name)!;

        /// <summary>
        /// E-3: a terrain with NO source is saved over its own base — works; the saved terrain holds the blocks; the kept ground has a name of its own (<c>base-t-ground</c>: <c>source/base/terrain.json</c> <c>name</c>, <c>terrain.json</c> <c>source.base.name</c>,
        /// the copied source scenario's header) and resolves through the catalogue to a world with NO block houses — so reopening the source loads the bare ground; the returned scenario stays on the terrain.
        /// 🔴 Red-proof: skip the rename ⇒ the source's ground name resolves to the saved terrain (blocks and all).
        /// </summary>
        [Fact]
        public void ASourceSavedOverItsOwnBase_KeepsAGroundOfItsOwnName_ThatResolvesWithoutTheBlocks()
        {
            using var f = Fixture();
            int before = f.BaseWorld().Buildings.Count;
            string oldHash = TerrainContentHash.OfTerrain(f.Definition);
            var r = TerrainBake.SaveAs(f.ScenarioDir, f.TerrainFolder, "base-t", f.Resolve, new[] { f.Recipes }, bakeNavtiles: false);
            Assert.Equal(Path.GetFullPath(f.TerrainFolder), Path.GetFullPath(r.Folder));
            Assert.False(Directory.Exists(f.TerrainFolder + ".part")); Assert.False(Directory.Exists(f.TerrainFolder + ".old"));
            Assert.Equal("base-t", (string)r.Scenario["header"]!["terrainName"]!);                                   // the new scenario stays on the terrain

            string keptDef = Path.Combine(f.TerrainFolder, "source", "base", "terrain.json");
            Assert.Equal("base-t-ground", TerrainCatalog.SourceBaseNameOf(f.TerrainFolder));
            Assert.Equal("base-t", TerrainCatalog.SourceBaseOfOf(f.TerrainFolder));
            Assert.Equal("base-t-ground", (string)JsonNode.Parse(File.ReadAllText(keptDef))!["name"]!);
            var sourceHeader = JsonNode.Parse(File.ReadAllText(Path.Combine(f.TerrainFolder, "source", "scenario", "scenario.json")))!["header"]!;
            Assert.Equal("base-t-ground", (string)sourceHeader["terrainName"]!);

            var catalog = new TerrainCatalog(new[] { Terrains(f) });
            Assert.Equal(keptDef, catalog.ResolveDefinition("base-t-ground"));
            Assert.Equal(before, WorldOf(keptDef, "base-t-ground").Buildings.Count);                              // the bare ground: no block houses
            Assert.True(WorldOf(f.Definition, "base-t").Buildings.Count > before, "the saved terrain holds the blocks");
            Assert.Equal(new[] { "base-t" }, catalog.List());

            // 📐 MEASURED: renaming the copy moves its content hash (terrain.json is hashed) — the recorded source.base.contentHash is the RENAMED copy's, which is what an overlay compiled against the kept ground will carry
            string keptHash = TerrainContentHash.OfTerrain(keptDef);
            Assert.NotEqual(oldHash, keptHash);
            Assert.Equal(keptHash, (string)JsonNode.Parse(File.ReadAllText(f.Definition))!["source"]!["base"]!["contentHash"]!);
        }

        /// <summary>
        /// A second save — from the KEPT source (its header names the ground), and from the ORIGINAL scenario (still naming the terrain) — gives the same terrain as the first (same hash and building count: the blocks are never stacked on a saved result),
        /// and the ground keeps its name. 🔴 Red-proof: resolve the original's name to the target itself ⇒ the second save holds the blocks twice.
        /// </summary>
        [Fact]
        public void ASecondSave_FromTheKeptSource_OrTheOriginal_GivesTheSameTerrain()
        {
            using var f = Fixture();
            var first = TerrainBake.SaveAs(f.ScenarioDir, f.TerrainFolder, "base-t", f.Resolve, new[] { f.Recipes }, bakeNavtiles: false);
            int firstCount = WorldOf(f.Definition, "base-t").Buildings.Count;
            var catalog = new TerrainCatalog(new[] { Terrains(f) });

            var fromKept = TerrainBake.SaveAs(Path.Combine(f.TerrainFolder, "source", "scenario"), f.TerrainFolder, "base-t", catalog.ResolveDefinition, new[] { f.Recipes }, bakeNavtiles: false);
            Assert.Equal(first.ContentHash, fromKept.ContentHash);
            Assert.Equal(firstCount, WorldOf(f.Definition, "base-t").Buildings.Count);
            Assert.Equal("base-t-ground", TerrainCatalog.SourceBaseNameOf(f.TerrainFolder));

            var fromOriginal = TerrainBake.SaveAs(f.ScenarioDir, f.TerrainFolder, "base-t", catalog.ResolveDefinition, new[] { f.Recipes }, bakeNavtiles: false);
            Assert.Equal(first.ContentHash, fromOriginal.ContentHash);
            Assert.Equal(firstCount, WorldOf(f.Definition, "base-t").Buildings.Count);
            Assert.Equal("base-t-ground", TerrainCatalog.SourceBaseNameOf(f.TerrainFolder));                      // the original's save keeps the renamed ground
            Assert.Equal("base-t-ground", (string)JsonNode.Parse(File.ReadAllText(Path.Combine(f.TerrainFolder, "source", "scenario", "scenario.json")))!["header"]!["terrainName"]!);
        }

        /// <summary>The kept ground's name is deterministic: <c>-ground</c> unless a terrain has it, then <c>-ground-2</c>, <c>-ground-3</c> …</summary>
        [Fact]
        public void TheKeptGroundsName_SkipsATakenName_Deterministically()
        {
            using var f = Fixture();
            foreach (var taken in new[] { "base-t-ground", "base-t-ground-2" })
            {
                Directory.CreateDirectory(Path.Combine(Terrains(f), taken));
                File.WriteAllText(Path.Combine(Terrains(f), taken, "terrain.json"), """{"schemaVersion":2}""");
            }
            var catalog = new TerrainCatalog(new[] { Terrains(f) });
            TerrainBake.SaveAs(f.ScenarioDir, f.TerrainFolder, "base-t", catalog.ResolveDefinition, new[] { f.Recipes }, bakeNavtiles: false);
            Assert.Equal("base-t-ground-3", TerrainCatalog.SourceBaseNameOf(f.TerrainFolder));
        }
    }
}

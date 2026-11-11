using System;
using System.IO;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18a) — <see cref="TerrainContentHash"/>: a content identity that is stable, sees a changed world byte, ignores the derived navtiles, and is cached by (length, mtime).</summary>
    public sealed class TerrainContentHashTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "tch-" + Guid.NewGuid().ToString("N"));
        private string Def => Path.Combine(_dir, "terrain.json");
        private string World => Path.Combine(_dir, "w.geojson");

        public TerrainContentHashTests()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "navtiles"));
            File.WriteAllText(Def, "{\"schemaVersion\":2,\"world\":\"w.geojson\"}");
            File.WriteAllText(World, "{\"type\":\"FeatureCollection\",\"features\":[]}");
        }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        /// <summary>🔴 Red-proof: hash only terrain.json ⇒ the changed-world-byte assertion fails; hash navtiles/ too ⇒ the navtiles assertion fails.</summary>
        [Fact]
        public void OfTerrain_IsStable_SeesAWorldByte_IgnoresNavtilesAndItsOwnCache()
        {
            string a = OfTerrain();
            Assert.Equal(64, a.Length);
            Assert.Equal(a, OfTerrain());
            Assert.True(File.Exists(Path.Combine(_dir, TerrainContentHash.CacheFileName)));

            File.WriteAllText(Path.Combine(_dir, "navtiles", "1_1_x.navtile"), "tile");                          // a derived cache: not content
            Directory.CreateDirectory(Path.Combine(_dir, "source")); File.WriteAllText(Path.Combine(_dir, "source", "raw.bin"), "raw");
            Assert.Equal(a, OfTerrain());

            File.WriteAllText(World, "{\"type\":\"FeatureCollection\",\"features\":[ ]}");                       // one byte more
            Assert.NotEqual(a, OfTerrain());
        }

        /// <summary>The cache is used: a world file changed back to the SAME length and mtime answers the cached hash (no world byte is read); a new mtime re-hashes. 🔴 Red-proof: ignore the cache ⇒ the first assertion fails.</summary>
        [Fact]
        public void OfTerrain_IsCachedByLengthAndMtime()
        {
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(World, t0);
            string a = OfTerrain();
            File.WriteAllText(World, "{\"type\":\"FeatureCollection\",\"features\":[]}".Replace("[]", "[ ]").Replace(" ]", "]").Replace("features", "featurez"));   // same length, other bytes
            Assert.Equal(new FileInfo(World).Length, "{\"type\":\"FeatureCollection\",\"features\":[]}".Length);
            File.SetLastWriteTimeUtc(World, t0);
            Assert.Equal(a, OfTerrain());                                                                         // served from terrain.hash
            File.SetLastWriteTimeUtc(World, t0.AddSeconds(1));
            Assert.NotEqual(a, OfTerrain());                                                                      // the entry no longer matches: re-hashed
        }

        [Fact]
        public void OfOverlay_HashesTheWorldAndBuildings_NotTheHeaderNorNavtiles()
        {
            string o = Path.Combine(_dir, "ov");
            Directory.CreateDirectory(Path.Combine(o, "buildings")); Directory.CreateDirectory(Path.Combine(o, "navtiles"));
            File.WriteAllText(Path.Combine(o, "overlay.json"), "{\"format\":1,\"contentHash\":\"x\"}");
            File.WriteAllText(Path.Combine(o, "overlay.world.geojson"), "{}");
            File.WriteAllText(Path.Combine(o, "buildings", "h.building.json"), "{}");
            string a = TerrainContentHash.OfOverlay(o);
            File.WriteAllText(Path.Combine(o, "overlay.json"), "{\"format\":1,\"contentHash\":\"other\"}");
            File.WriteAllText(Path.Combine(o, "navtiles", "t.navtile"), "t");
            Assert.Equal(a, TerrainContentHash.OfOverlay(o));
            File.WriteAllText(Path.Combine(o, "buildings", "h.building.json"), "{ }");
            Assert.NotEqual(a, TerrainContentHash.OfOverlay(o));
        }

        /// <summary>⭐ The SAME parts over two different BASE terrains are two overlays: the staged folder is named by this hash
        /// (<c>Terrain/overlays/&lt;hash&gt;</c>) and the two hold different navtiles, so they must never share it. 🔴 Mutation: the base hash
        /// left out of the hash ⇒ equal ⇒ red.</summary>
        [Fact]
        public void OfOverlay_TheSamePartsOverAnotherBase_IsAnotherOverlay()
        {
            string o = Path.Combine(_dir, "ovb");
            Directory.CreateDirectory(o);
            File.WriteAllText(Path.Combine(o, "overlay.world.geojson"), "{}");
            string Header(string baseHash) => "{\"format\":1,\"contentHash\":\"x\",\"base\":{\"name\":\"t\",\"contentHash\":\"" + baseHash
                + "\"},\"scenario\":\"s\",\"world\":\"overlay.world.geojson\"}";
            File.WriteAllText(Path.Combine(o, "overlay.json"), Header("aaaa"));
            string onA = TerrainContentHash.OfOverlay(o);
            File.WriteAllText(Path.Combine(o, "overlay.json"), Header("bbbb"));
            Assert.NotEqual(onA, TerrainContentHash.OfOverlay(o));
        }

        private string OfTerrain() => TerrainContentHash.OfTerrain(Def);
    }
}

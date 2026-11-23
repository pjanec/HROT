using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkits.Tests.Terrain
{
    /// <summary>The process-wide hook is static: the classes that set it never run in parallel with each other or with anything else.</summary>
    [CollectionDefinition("TerrainSources", DisableParallelization = true)]
    public sealed class TerrainSourcesCollection { }

    /// <summary>
    /// ⭐ CE-11037 (docs/DESIGN_Terrain_Import_UTM.md §13 F-1) — what <see cref="TerrainCatalog"/> does with <see cref="TerrainSources.Import"/>:
    /// consulted INSIDE the catalog on a miss and on a hit, never a host's job. (The importer behind it: <c>Hrot.Terrain.Import.Tests/SourceImportTests</c>.)
    /// </summary>
    [Collection("TerrainSources")]
    public sealed class TerrainSourcesTests : IDisposable
    {
        private readonly string _a = Path.Combine(Path.GetTempPath(), "ce11037-a-" + Guid.NewGuid().ToString("N")[..8]);
        private readonly string _b = Path.Combine(Path.GetTempPath(), "ce11037-b-" + Guid.NewGuid().ToString("N")[..8]);

        public void Dispose()
        {
            TerrainSources.Import = null;
            foreach (var d in new[] { _a, _b }) try { Directory.Delete(d, true); } catch (IOException) { }
        }

        private sealed class Fake : ITerrainSourceImport
        {
            public readonly List<(string Name, string? Existing, string Target)> Calls = new();
            public Func<string, string?, string, string?> Answer = (_, _, _) => null;
            public string? TryImport(string terrainName, string? existingDefinition, string targetRoot, IReadOnlyList<string> catalogRoots, CancellationToken cancellation)
            {
                Calls.Add((terrainName, existingDefinition, targetRoot));
                return Answer(terrainName, existingDefinition, targetRoot);
            }
        }

        private static string Make(string root, string name)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, TerrainCatalog.DefinitionFileName);
            File.WriteAllText(path, "{ \"schemaVersion\": 2, \"name\": \"" + name + "\" }");
            return path;
        }

        [Fact]
        public void NoHook_AMiss_IsNull_AsToday()
        {
            TerrainSources.Import = null;
            Assert.Null(new TerrainCatalog(new[] { _a }).ResolveDefinition("ghost"));
            Assert.False(Directory.Exists(_a));   // a catalog with no hook creates nothing
        }

        [Fact]
        public void AMiss_AsksTheHook_WithTheFirstWritableRoot_AndReturnsItsAnswer()
        {
            var fake = new Fake { Answer = (n, _, root) => Make(root, n) };
            TerrainSources.Import = fake;
            var path = new TerrainCatalog(new[] { _a, _b }).ResolveDefinition("town");
            Assert.Equal(Path.Combine(_a, "town", "terrain.json"), path);
            var call = Assert.Single(fake.Calls);
            Assert.Equal(("town", (string?)null, _a), call);
        }

        [Fact]
        public void AMiss_ThatTheHookDeclines_IsNull_AndCreatesNoTerrain()
        {
            var fake = new Fake();
            TerrainSources.Import = fake;
            Assert.Null(new TerrainCatalog(new[] { _a }).ResolveDefinition("ghost"));
            Assert.Single(fake.Calls);
        }

        [Fact]
        public void AHit_IsStillOffered_ToTheHook_WithTheExistingDefinition_AndItsRoot()
        {
            string existing = Make(_b, "town");
            var fake = new Fake();   // declines: the terrain is current
            TerrainSources.Import = fake;
            Assert.Equal(existing, new TerrainCatalog(new[] { _a, _b }).ResolveDefinition("town"));
            Assert.Equal(("town", (string?)existing, _b), Assert.Single(fake.Calls));   // the root that HOLDS it is the target, not the first writable one
        }

        [Fact]
        public void AHit_TheHookReImported_ReturnsTheNewDefinition()
        {
            string existing = Make(_a, "town");
            string fresh = Path.Combine(_b, "fresh.json");
            TerrainSources.Import = new Fake { Answer = (_, _, _) => fresh };
            Assert.Equal(fresh, new TerrainCatalog(new[] { _a }).ResolveDefinition("town"));
            Assert.True(File.Exists(existing));
        }

        [Fact]
        public void AHookFailure_IsLoud_NotANull()
        {
            TerrainSources.Import = new Fake { Answer = (_, _, _) => throw new InvalidDataException("bad input set") };
            Assert.Throws<InvalidDataException>(() => new TerrainCatalog(new[] { _a }).ResolveDefinition("town"));
        }

        [Fact]
        public void AKeptGround_IsResolvedBeforeTheHookIsAsked()
        {
            string holder = Path.Combine(_a, "town");
            Directory.CreateDirectory(Path.Combine(holder, "source", "base"));
            File.WriteAllText(Path.Combine(holder, "terrain.json"), "{ \"schemaVersion\": 2, \"name\": \"town\", \"source\": { \"base\": { \"name\": \"ground\" } } }");
            string kept = Path.Combine(holder, "source", "base", "terrain.json");
            File.WriteAllText(kept, "{ \"schemaVersion\": 2, \"name\": \"ground\" }");
            var fake = new Fake();
            TerrainSources.Import = fake;
            Assert.Equal(kept, new TerrainCatalog(new[] { _a }).ResolveDefinition("ground"));
            Assert.Empty(fake.Calls);
        }
    }
}

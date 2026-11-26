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
            TerrainSources.SharedImportRoot = null;
            foreach (var d in new[] { _a, _b }) try { Directory.Delete(d, true); } catch (IOException) { }
        }

        private sealed class Fake : ITerrainSourceImport
        {
            public readonly List<(string Name, string? Existing, string Target)> Calls = new();
            public Func<string, string?, string, string?> Answer = (_, _, _) => null;
            public string? TryImport(string terrainName, string? existingDefinition, Func<string> targetRoot, IReadOnlyList<string> catalogRoots, CancellationToken cancellation)
            {
                string target = targetRoot();
                Calls.Add((terrainName, existingDefinition, target));
                return Answer(terrainName, existingDefinition, target);
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

        /// <summary>
        /// ⭐ CE-11039 (F-2 revised) — a node catalog (staging root + NAS) asks the hook to import into the NAS's <c>assets/terrain</c> (the shared root), never the node's private staging root;
        /// with no shared root named the first writable root is still the target; an unwritable shared root falls back to the staging root (here its path is a file).
        /// 🔴 Mutation: <c>ImportTarget</c> returning the first writable root ⇒ the first cell reds (staging); ignoring the writable probe ⇒ the third.
        /// </summary>
        [Fact]
        public void TheImportTarget_IsTheSharedRoot_WhenWritable_ElseTheFirstWritableRoot()
        {
            var fake = new Fake();
            TerrainSources.Import = fake;
            string nas = Path.Combine(_b, "nas");
            Assert.Null(TerrainCatalog.ForNode(_a, nas).ResolveDefinition("town"));
            Assert.Equal(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(nas), Assert.Single(fake.Calls).Target);

            fake.Calls.Clear();
            Assert.Null(new TerrainCatalog(new[] { _a, _b }).ResolveDefinition("town"));   // no shared root named: the old rule
            Assert.Equal(_a, Assert.Single(fake.Calls).Target);

            fake.Calls.Clear();
            string nas2 = Path.Combine(_b, "nas2");
            Directory.CreateDirectory(Path.Combine(nas2, "assets"));
            File.WriteAllText(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(nas2), "a file, so no folder can be made there");
            Assert.Null(TerrainCatalog.ForNode(_a, nas2).ResolveDefinition("town"));
            Assert.Equal(Path.Combine(_a, TerrainCatalog.StagingDirectoryName), Assert.Single(fake.Calls).Target);
        }

        /// <summary>
        /// ⭐ CE-11039 — the process-wide machine root (a dev checkout's <c>assets/terrain</c>) is the import target ahead of the NAS, and a terrain it holds is FOUND by a catalog whose own roots lack it
        /// (the hit path: offered to the hook with that root as the target). 🔴 Mutation: the target or the lookup ignoring it ⇒ the first or the second cell reds.
        /// </summary>
        [Fact]
        public void TheMachineRoot_IsTheTargetAheadOfTheNas_AndIsLookedIn()
        {
            string machine = Path.Combine(_b, "machine");
            TerrainSources.SharedImportRoot = machine;
            var fake = new Fake();
            TerrainSources.Import = fake;
            Assert.Null(TerrainCatalog.ForNode(_a, Path.Combine(_b, "nas")).ResolveDefinition("town"));
            Assert.Equal(machine, Assert.Single(fake.Calls).Target);

            string held = Make(machine, "town");
            fake.Calls.Clear();
            Assert.Equal(held, TerrainCatalog.ForNode(_a, Path.Combine(_b, "nas")).ResolveDefinition("town"));   // another staging root, its own NAS: found in the machine root
            Assert.Equal(("town", (string?)held, machine), Assert.Single(fake.Calls));
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

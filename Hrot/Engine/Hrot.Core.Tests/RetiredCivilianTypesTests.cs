using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fdp.Toolkit.Tkb;
using Hrot.Core;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3220</c> part 2 (civilians B-2, B2-c, R-3013; docs/DESIGN_Civilians_And_Crowd_Control.md §7.2b) — the old pedestrian
    /// (1001) and child (1003) are RETIRED, never reused: people are the civilian family (1101–1104). ⛔ No compatibility alias —
    /// two ids for one thing. ✅ Red-proof: the 1001 template restored ⇒ the first case reds; a shipped scenario re-pointed at 1001
    /// ⇒ the second reds (and names the file).
    /// </summary>
    public class RetiredCivilianTypesTests
    {
        private static readonly long[] Retired = { 1001, 1003 };

        private static TkbDatabase BuiltIn()
        {
            var db = new TkbDatabase();
            HrotEnvironment.RegisterBuiltInTkb(db);
            return db;
        }

        [Fact]
        public void CE3220_TheBuiltInTkb_HasNoRetiredCivilianType()
        {
            var db = BuiltIn();
            foreach (var type in Retired)
                Assert.False(db.TryGetByType(type, out _), $"TKB type {type} is retired (CE-3220) and must not be registered");
            Assert.DoesNotContain(db.GetAll(), t => t.Name is "CivilianPedestrian" or "CivilianChild");
            Assert.True(db.TryGetByType(CivilianTkbCatalog.TkbCivilianAdultMale, out _), "the civilian family replaces them");
        }

        /// <summary>
        /// ⭐ Round-out: every <c>TkbType</c> a SHIPPED scenario places resolves in the built-in TKB — a retired id (or any id
        /// nobody registers) would load as an entity with no template on every node.
        /// </summary>
        [Fact]
        public void CE3220_EveryShippedScenario_PlacesOnlyBuiltInTypes()
        {
            var db = BuiltIn();
            var files = ShippedScenarios().ToList();
            Assert.True(files.Count >= 5, $"found only {files.Count} shipped scenarios — the search roots moved");
            var unresolved = new List<string>();
            foreach (var file in files)
                foreach (var type in TkbTypesIn(file))
                    if (!db.TryGetByType(type, out _))
                        unresolved.Add($"{Path.GetRelativePath(RepoRoot(), file)}: {type}");
            Assert.True(unresolved.Count == 0, "unregistered TkbTypes placed by shipped scenarios:\n" + string.Join("\n", unresolved));
        }

        private static IEnumerable<string> ShippedScenarios()
        {
            var root = RepoRoot();
            foreach (var dir in new[] { Path.Combine(root, "scenarios"),
                                        Path.Combine(root, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios") })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.EnumerateFiles(dir, "scenario.json", SearchOption.AllDirectories))
                        yield return f;
        }

        private static IEnumerable<long> TkbTypesIn(string file)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Object) yield break;
            foreach (var e in entities.EnumerateObject())
                if (e.Value.TryGetProperty("TkbIdentity", out var id) && id.TryGetProperty("TkbType", out var t) && t.TryGetInt64(out long type))
                    yield return type;
        }

        private static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("repo root not found");
        }
    }
}

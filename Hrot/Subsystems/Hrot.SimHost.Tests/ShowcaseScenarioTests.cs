using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Testing;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ The SHOWCASE scenarios (📄 docs/SHOWCASE_Scenarios.md, the showcase handoff) — the acceptance lines a checks.json cannot carry (it runs one
    /// variant): §1.6 line 3, <i>"the same variant number produces the same outpost position, QRF edge and civilians"</i>. Read from the REAL files
    /// through <see cref="ScenarioParams.Prepare"/>, the ONE routine every node's load runs (macros S3b B2, SR-101 ④).
    /// </summary>
    public sealed class ShowcaseScenarioTests
    {
        private static readonly string Bridgehead = File.ReadAllText(Path.Combine(TestRepo.Root, "scenarios", "showcase-bridgehead", "scenario.json"));

        /// <summary>What a variant decides in Bridgehead: the outpost instance's place, the QRF start marker's place, the villagers present.</summary>
        private static string Choices(int variant)
        {
            var dom = (JsonObject)JsonNode.Parse(Bridgehead)!;
            string args = variant == 0 ? "" : "{\"$variant\": " + variant + "}";
            ScenarioParams.Prepare(dom, ScenarioHeaderJson.Read(dom), args, null, "scenario 'showcase-bridgehead'", variant);
            var ents = ((JsonObject)dom["entities"]!).Select(kv => (JsonObject)kv.Value!).ToList();
            string At(string name)
            {
                var e = ents.Single(x => x["EntityInfo"]?["Name"]?.GetValue<string>() == name);
                var p = (JsonArray)e["SimTransform"]!["Position"]!;
                return $"{p[0]!.GetValue<double>():0},{p[1]!.GetValue<double>():0}";
            }
            var villagers = ents.Select(x => x["EntityInfo"]?["Name"]?.GetValue<string>()).Where(n => n != null && n.StartsWith("Villager", StringComparison.Ordinal));
            Assert.DoesNotContain("\"Positions\"", dom.ToJsonString());
            Assert.DoesNotContain("\"Presence\"", dom.ToJsonString());
            return $"outpost {At("Red outpost")} | qrf {At("QRF start")} | {string.Join(",", villagers.OrderBy(n => n, StringComparer.Ordinal))}";
        }

        /// <summary>⭐ §1.6 line 3 — one variant twice is the same exercise (30 variants), variant 0 is as authored (the first place, every villager),
        /// and across variants every outpost place, both QRF edges and a missing villager occur. 🔴 Red-proof: a random place pick ⇒ the twice-the-same
        /// assertion goes red; the file's Positions removed ⇒ only one outpost place occurs.</summary>
        [Fact]
        public void Bridgehead_OneVariant_IsOneExercise_AndVariantsDiffer()
        {
            Assert.Equal("outpost 65,114 | qrf 322,120 | Villager 1,Villager 2,Villager 3,Villager 4,Villager 5,Villager 6", Choices(0));
            var outposts = new HashSet<string>(); var edges = new HashSet<string>(); bool aVillagerMissing = false;
            for (int v = 1; v <= 30; v++)
            {
                string c = Choices(v);
                Assert.Equal(c, Choices(v));
                var parts = c.Split(" | ");
                outposts.Add(parts[0]); edges.Add(parts[1]);
                if (parts[2].Split(',').Length < 6) aVillagerMissing = true;
            }
            Assert.Equal(3, outposts.Count);
            Assert.Equal(2, edges.Count);
            Assert.True(aVillagerMissing);
        }
    }
}

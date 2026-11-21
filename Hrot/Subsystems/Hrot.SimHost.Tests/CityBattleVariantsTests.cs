using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Testing;
using Fdp.Toolkit.Scenario;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-6011</c> (city battle V-A; 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4c; docs/DESIGN_Scenario_Macros.md §17) —
    /// the city battle's VARIANT FORMS through the ONE routine every node's load runs (<see cref="ScenarioVariants.Apply"/>): variant 0 is as authored; the
    /// variants differ in exactly the four places the design names — the defender layout, the roadblock set, the air-defence positions, the civilians'
    /// density — and NOWHERE else (the attacker, the triggers, the objectives are the same in every variant).
    /// </summary>
    public sealed class CityBattleVariantsTests
    {
        private readonly ITestOutputHelper _out;
        public CityBattleVariantsTests(ITestOutputHelper output) => _out = output;

        private static readonly JsonObject File0 = JsonNode.Parse(File.ReadAllText(TestRepo.ScenarioFile("city-battle")))!.AsObject();
        private static readonly string[] Defenders = { "1 Coy (D)", "2 Coy (D)", "3 Coy (D)", "Reserve Coy (D)" };
        private static readonly string[] AirDefence = { "ZU-23 Technical", "Stinger 1", "Stinger 2", "Igla 1", "Igla 2" };

        private sealed record Outcome(string Layout, string Roadblocks, string Air, int Civilians, string Rest);

        private static string Name(JsonObject e) => (string?)e["EntityInfo"]?["Name"] ?? "";
        private static string Where(JsonObject e) => string.Join(",", e["SimTransform"]!["Position"]!.AsArray().Take(2).Select(x => ((double)x!).ToString("0")));

        private static Outcome Apply(int variant)
        {
            var dom = File0.DeepClone().AsObject();
            ScenarioVariants.Apply(dom, variant, Guid.Empty, "city-battle");
            var es = dom["entities"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.AsObject());
            string Of(IEnumerable<string> names) => string.Join(" ", names.Select(n => es.Values.FirstOrDefault(e => Name(e) == n) is { } e ? $"{n}@{Where(e)}" : $"{n}:-"));
            var roadblocks = string.Join(" ", es.Values.Where(e => Name(e).StartsWith("Roadblock ")).Select(Name).OrderBy(n => n));
            int civilians = es.Values.Count(e => Name(e).StartsWith("Civilian "));
            // everything the variant forms do not touch, minus the bookkeeping Apply adds (VariantForms)
            var rest = string.Join("\n", es.Where(kv => !Name(kv.Value).StartsWith("Civilian ") && !Name(kv.Value).StartsWith("Roadblock ")
                                                         && !Defenders.Contains(Name(kv.Value)) && !AirDefence.Contains(Name(kv.Value)))
                                          .OrderBy(kv => kv.Key).Select(kv => kv.Key + kv.Value.ToJsonString()));
            return new Outcome(Of(Defenders), roadblocks, Of(AirDefence), civilians, rest);
        }

        /// <summary>⭐ <c>CE6011_V1</c> — variant 0 is AS AUTHORED: three roadblocks, 200 civilians, every company and AA team at its first place.
        /// 🔴 Red-proof: the generator putting an alternative first ⇒ red.</summary>
        [Fact]
        public void CE6011_V1_Variant0_IsAsAuthored()
        {
            var o = Apply(0);
            Assert.Equal("Roadblock C Roadblock N Roadblock S", o.Roadblocks);
            Assert.Equal(200, o.Civilians);
            Assert.Equal("1 Coy (D)@690,950 2 Coy (D)@640,750 3 Coy (D)@690,560 Reserve Coy (D)@1250,750", o.Layout);
            Assert.StartsWith("ZU-23 Technical@1390,790", o.Air);
        }

        /// <summary>
        /// ⭐⭐ <c>CE6011_V2</c> — over variants 1…30 each of the four dimensions takes MORE than one value (they really vary), two variants differ in all four
        /// (named in the output), and everything else is IDENTICAL in every variant. 🔴 Red-proof: drop the <c>Positions</c> from the defender companies ⇒ the
        /// layout never varies ⇒ red.
        /// </summary>
        [Fact]
        public void CE6011_V2_TheVariantsDiffer_InTheFourPlaces_AndNowhereElse()
        {
            var all = Enumerable.Range(1, 30).Select(v => (v, o: Apply(v))).ToList();
            var baseline = Apply(0);
            Assert.True(all.Select(x => x.o.Layout).Distinct().Count() > 1, "the defender layout varies");
            Assert.True(all.Select(x => x.o.Roadblocks).Distinct().Count() > 1, "the roadblock set varies");
            Assert.True(all.Select(x => x.o.Air).Distinct().Count() > 1, "the air-defence positions vary");
            Assert.True(all.Select(x => x.o.Civilians).Distinct().Count() > 1, "the civilian density varies");
            Assert.All(all, x => Assert.True(x.o.Rest == baseline.Rest, $"variant {x.v} changed something outside the four forms"));
            var pair = (from a in all from b in all
                        where a.v < b.v && a.o.Layout != b.o.Layout && a.o.Roadblocks != b.o.Roadblocks && a.o.Air != b.o.Air && a.o.Civilians != b.o.Civilians
                        select (a, b)).First();
            foreach (var (v, o) in new[] { pair.a, pair.b })
                _out.WriteLine($"variant {v}: [{o.Roadblocks}] · {o.Civilians} civilians · {o.Layout} · {o.Air}");
        }
    }
}

using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐⭐ Macros S3a ⑦ (docs/DESIGN_Scenario_Macros.md SR-41, SR-28, SR-149) — <see cref="ScenarioParams"/>, the ONE routine both a macro expansion
    /// and a scenario load use: the header's defaults ▸ the args, an undeclared arg refused, the file's <c>ParamBindings</c> applied to its entities.
    /// </summary>
    public sealed class ScenarioParamsTests
    {
        private const string File = """
            { "header": {"Params": [{"Name": "side", "TypeId": "ForceId", "Default": "Hostile"}, {"Name": "count", "TypeId": "int", "Default": 2}]},
              "entities": { "aaaa0000-0000-4000-8000-000000000001": {
                  "EntityInfo": {"Name": "Lead", "ForceId": "Neutral"},
                  "ParamBindings": {"Bindings": {"EntityInfo.ForceId": "side"}} } } }
            """;

        private static (JsonObject Dom, ScenarioParams.Prepared Bound) Prepare(string? args)
        {
            var dom = JsonNode.Parse(File)!.AsObject();
            return (dom, ScenarioParams.Prepare(dom, ScenarioHeaderJson.Read(dom), args, null, "scenario 'x'"));
        }

        private static string ForceOf(JsonObject dom)
            => dom["entities"]!["aaaa0000-0000-4000-8000-000000000001"]!["EntityInfo"]!["ForceId"]!.GetValue<string>();

        [Fact]
        public void NoArgs_TheDefaultsAreBound()
        {
            var (dom, bound) = Prepare(null);
            Assert.Equal("Hostile", ForceOf(dom));
            var effective = JsonNode.Parse(bound.EffectiveJson)!.AsObject();
            Assert.Equal("Hostile", effective["side"]!.GetValue<string>());
            Assert.Equal(2, effective["count"]!.GetValue<int>());
        }

        [Fact]
        public void AnArg_OverridesItsDefault_TheOthersStay()
        {
            var (dom, bound) = Prepare("""{"side": "Friend"}""");
            Assert.Equal("Friend", ForceOf(dom));
            Assert.Contains("\"count\":2", bound.EffectiveJson);
        }

        [Fact]
        public void AnUndeclaredArg_OrArgsThatAreNotAnObject_AreRefused_NamingTheFile()
        {
            var ex = Assert.Throws<ScenarioParams.ArgsRefused>(() => Prepare("""{"colour": "red"}"""));
            Assert.Contains("scenario 'x': arg 'colour' is not a param", ex.Message);
            Assert.Throws<ScenarioParams.ArgsRefused>(() => Prepare("[1, 2]"));
            Assert.Throws<ScenarioParams.ArgsRefused>(() => Prepare("{not json"));
        }

        [Fact]
        public void Applies_OnlyWhenTheFileDeclaresParams_OrArgsAreGiven()
        {
            Assert.False(ScenarioParams.Applies(new ScenarioHeader(""), null));
            Assert.True(ScenarioParams.Applies(new ScenarioHeader(""), """{"a": 1}"""));
            Assert.True(ScenarioParams.Applies(ScenarioHeaderJson.Read(JsonNode.Parse(File)!.AsObject()), null));
        }

        // ════════ ⭐ Macros S3b B2 (SR-101) — RUN VARIANTS: presence and one-of-N, decided by the variant alone ════════

        private const string VariantFile = """
            { "header": {},
              "entities": {
                "cccc0000-0000-4000-8000-000000000001": { "EntityInfo": {"Name": "always"} },
                "cccc0000-0000-4000-8000-000000000002": { "EntityInfo": {"Name": "maybe"}, "Presence": 0.5 },
                "cccc0000-0000-4000-8000-000000000003": { "EntityInfo": {"Name": "never"}, "Presence": 0 },
                "cccc0000-0000-4000-8000-000000000004": { "EntityInfo": {"Name": "qrf"},
                    "MacroInstance": {"Macro": ["forces/A", {"macro": "forces/B", "weight": 3}, {"macro": "forces/Never", "weight": 0}], "Args": {}} } } }
            """;

        private static (JsonObject Dom, ScenarioParams.Prepared Bound) PrepareVariant(int variant, System.Guid salt = default)
        {
            var dom = (JsonObject)JsonNode.Parse(VariantFile)!;
            string args = variant == 0 ? "" : "{\"$variant\": " + variant + "}";
            return (dom, ScenarioParams.Prepare(dom, ScenarioHeaderJson.Read(dom), args, null, "scenario 'v'", variant, salt));
        }

        private static string? MacroOf(JsonObject dom)
            => ((JsonObject)dom["entities"]!)["cccc0000-0000-4000-8000-000000000004"]?["MacroInstance"]?["Macro"]?.GetValue<string>();

        /// <summary>⭐ B2 — variant 0 is AS AUTHORED: everything present (even Presence 0 — the editor shows the authored layout), the FIRST choice;
        /// the Presence keys are stripped; the reserved "$variant" arg is never refused as undeclared.</summary>
        [Fact]
        public void B2_VariantZero_IsAsAuthored()
        {
            var (dom, _) = PrepareVariant(0);
            var e = (JsonObject)dom["entities"]!;
            Assert.Equal(4, e.Count);
            Assert.Equal("forces/A", MacroOf(dom));
            Assert.DoesNotContain("Presence", dom.ToJsonString());
        }

        /// <summary>⭐ B2 — a variant decides presence and the choice ALONE: the same variant twice is the same file, the run records it, Presence 0
        /// is never there, a weight of 0 is never chosen, and across variants both outcomes of a 0.5 presence occur. 🔴 Red-proof: roll by a fresh
        /// random instead of the variant ⇒ the twice-the-same assertion goes red.</summary>
        [Fact]
        public void B2_AVariant_DecidesAlone_AndIsRecorded()
        {
            var (a, bound) = PrepareVariant(7);
            for (int v = 1; v <= 30; v++)                                                   // every variant, twice: the SAME file (a random roll cannot pass 30 of these)
                Assert.Equal(PrepareVariant(v).Dom.ToJsonString(), PrepareVariant(v).Dom.ToJsonString());
            Assert.Contains("\"$variant\":7", bound.EffectiveJson);

            bool sawMaybe = false, sawNoMaybe = false;
            for (int v = 1; v <= 60; v++)
            {
                var (d, _) = PrepareVariant(v);
                var e = (JsonObject)d["entities"]!;
                Assert.False(e.ContainsKey("cccc0000-0000-4000-8000-000000000003"));        // Presence 0
                Assert.True(e.ContainsKey("cccc0000-0000-4000-8000-000000000001"));         // no Presence: always
                Assert.NotEqual("forces/Never", MacroOf(d));                                // weight 0
                if (e.ContainsKey("cccc0000-0000-4000-8000-000000000002")) sawMaybe = true; else sawNoMaybe = true;
            }
            Assert.True(sawMaybe && sawNoMaybe);
        }

        /// <summary>⭐ B2 — inside a macro the roll is salted by the INSTANCE: two instances of one macro under one variant decide apart.</summary>
        [Fact]
        public void B2_TwoInstances_RollApart()
        {
            int differ = 0;
            for (int v = 1; v <= 40; v++)
            {
                var (x, _) = PrepareVariant(v, new System.Guid("11111111-0000-4000-8000-000000000001"));
                var (y, _) = PrepareVariant(v, new System.Guid("22222222-0000-4000-8000-000000000002"));
                if (x.ToJsonString() != y.ToJsonString()) differ++;
            }
            Assert.True(differ > 0);
        }
        // ════════ ⭐ SR-101 ④ (showcase scenarios) — ONE of N places, decided by the variant alone ════════

        private const string PlacesFile = """
            { "header": {},
              "entities": {
                "dddd0000-0000-4000-8000-000000000001": { "EntityInfo": {"Name": "outpost"},
                    "SimTransform": {"Position": [0, 0, 0], "Rotation": [0, 0, 0, 1]},
                    "Positions": [[10, 20, 0], {"at": [30, 40], "heading": 90}, {"at": [50, 60, 1], "weight": 2}, {"at": [70, 80, 0], "weight": 0}] },
                "dddd0000-0000-4000-8000-000000000002": { "EntityInfo": {"Name": "marker"}, "Positions": [[1, 2, 3], [4, 5, 6]] } } }
            """;

        private static JsonObject PreparePlaces(int variant, string file = PlacesFile)
        {
            var dom = (JsonObject)JsonNode.Parse(file)!;
            string args = variant == 0 ? "" : "{\"$variant\": " + variant + "}";
            ScenarioParams.Prepare(dom, ScenarioHeaderJson.Read(dom), args, null, "scenario 'p'", variant, default);
            return dom;
        }

        private static double[] PositionOf(JsonObject dom, string id)
        {
            var p = (JsonArray)dom["entities"]![id]!["SimTransform"]!["Position"]!;
            return new[] { p[0]!.GetValue<double>(), p[1]!.GetValue<double>(), p[2]!.GetValue<double>() };
        }

        /// <summary>⭐ SR-101 ④ — variant 0 stands at the FIRST place; the Positions key is stripped; an entity with no SimTransform gets one;
        /// the file needs preparing even with no params and no args (<see cref="ScenarioVariants.Uses"/>).</summary>
        [Fact]
        public void Positions_VariantZero_IsTheFirstPlace_AndTheKeyIsStripped()
        {
            Assert.True(ScenarioVariants.Uses((JsonObject)JsonNode.Parse(PlacesFile)!));
            var dom = PreparePlaces(0);
            Assert.Equal(new double[] { 10, 20, 0 }, PositionOf(dom, "dddd0000-0000-4000-8000-000000000001"));
            Assert.Equal(new double[] { 1, 2, 3 }, PositionOf(dom, "dddd0000-0000-4000-8000-000000000002"));
            Assert.DoesNotContain("Positions", dom.ToJsonString());
        }

        /// <summary>⭐ SR-101 ④ — a variant decides the place ALONE (the same variant twice ⇒ the same place, 30 times), a weight of 0 is never
        /// chosen, every other place occurs across variants, and a place's heading turns the entity (90° = east). 🔴 Red-proof: pick by a fresh random
        /// ⇒ the twice-the-same assertion goes red; ignore the weights ⇒ [70, 80] appears.</summary>
        [Fact]
        public void Positions_AVariant_DecidesThePlaceAlone_WeightedAndTurned()
        {
            const string id = "dddd0000-0000-4000-8000-000000000001";
            var seen = new HashSet<string>();
            for (int v = 1; v <= 60; v++)
            {
                Assert.Equal(PreparePlaces(v).ToJsonString(), PreparePlaces(v).ToJsonString());
                var dom = PreparePlaces(v);
                var at = PositionOf(dom, id);
                seen.Add($"{at[0]},{at[1]}");
                if (at[0] == 30)
                {
                    var q = (JsonArray)dom["entities"]![id]!["SimTransform"]!["Rotation"]!;
                    var rot = new System.Numerics.Quaternion(q[0]!.GetValue<float>(), q[1]!.GetValue<float>(), q[2]!.GetValue<float>(), q[3]!.GetValue<float>());
                    Assert.Equal(90f, Fdp.Core.WorldRotation.ToHeadingDeg(rot), 1);
                }
            }
            Assert.DoesNotContain("70,80", seen);
            Assert.Equal(3, seen.Count);
        }

        /// <summary>⭐ SR-101 ④ — a malformed place list is refused naming the file and the entity, never applied half.</summary>
        [Fact]
        public void Positions_Malformed_IsRefused()
        {
            const string bad = """{ "header": {}, "entities": { "eeee0000-0000-4000-8000-000000000001": { "Positions": [[1]] } } }""";
            var ex = Assert.Throws<ScenarioParams.ArgsRefused>(() => PreparePlaces(3, bad));
            Assert.Contains("scenario 'p'", ex.Message);
            Assert.Contains("eeee0000-0000-4000-8000-000000000001", ex.Message);
        }
    }
}

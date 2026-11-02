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
    }
}

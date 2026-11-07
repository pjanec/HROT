using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐⭐ CE-3374 (📄 docs/DESIGN_Scenario_Macros.md §17.1) — the variant forms SURVIVE an edit load + save: <c>Apply</c> copies Presence / Positions / a one-of-N
    /// macro into <see cref="VariantForms"/> before it strips them; the translators write them back AS AUTHORED (the chosen place = the entity's current place,
    /// the chosen macro = the current one); a world built with a variant ≠ 0 is not saveable, and the gate says which variant.
    /// 🔴 Red-proof (compiles): <c>Apply</c> not writing the holder ⇒ no VariantForms ⇒ every test below red.
    /// </summary>
    public class VariantFormsTests
    {
        private const string File = """
            { "header": {},
              "entities": {
                "cccc0000-0000-4000-8000-000000000001": { "EntityInfo": {"Name": "plain"} },
                "cccc0000-0000-4000-8000-000000000002": { "EntityInfo": {"Name": "maybe"}, "Presence": 0.5,
                    "Positions": [[10, 20, 0], {"at": [30, 40, 0], "heading": 90, "weight": 2}] },
                "cccc0000-0000-4000-8000-000000000004": { "EntityInfo": {"Name": "qrf"},
                    "MacroInstance": {"Macro": ["forces/A", {"macro": "forces/B", "weight": 3}], "Args": {}} } } }
            """;

        private static JsonObject Prepared(int variant)
        {
            var dom = (JsonObject)JsonNode.Parse(File)!;
            string args = variant == 0 ? "" : "{\"$variant\": " + variant + "}";
            ScenarioParams.Prepare(dom, ScenarioHeaderJson.Read(dom), args, null, "scenario 'v'", variant, default);
            return dom;
        }

        private static JsonObject Entity(JsonObject dom, string id) => (JsonObject)((JsonObject)dom["entities"]!)[id]!;

        private static Dictionary<string, object> Data(JsonObject entity)
        {
            var d = new Dictionary<string, object>();
            foreach (var (k, v) in entity) d[k] = v!.DeepClone();
            return d;
        }

        [Fact]
        public void Apply_CopiesTheFormsIntoTheHolder_OnlyOnEntitiesThatHaveThem()
        {
            var dom = Prepared(0);
            Assert.False(Entity(dom, "cccc0000-0000-4000-8000-000000000001").ContainsKey(VariantForms.ComponentName));
            var vf = VariantForms.FromJson((JsonObject)Entity(dom, "cccc0000-0000-4000-8000-000000000002")[VariantForms.ComponentName]!);
            Assert.Equal(0, vf.Variant);
            Assert.Equal(0.5, vf.Presence);
            Assert.Equal(0, vf.PositionChosen);
            Assert.Contains("\"heading\":90", vf.PositionsJson);
            var macro = VariantForms.FromJson((JsonObject)Entity(dom, "cccc0000-0000-4000-8000-000000000004")[VariantForms.ComponentName]!);
            Assert.Contains("forces/B", macro.MacroChoicesJson);
        }

        [Fact]
        public void ASave_WritesTheFormsBackAsAuthored_AndAMoveMovesTheChosenPlace()
        {
            var dom = Prepared(0);
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            var e = repo.CreateEntity();
            var t = new VariantFormsTranslator();
            t.Inject(repo, e, Data(Entity(dom, "cccc0000-0000-4000-8000-000000000002")), null!);
            repo.AddComponent(e, new SimTransform { Position = new WorldPoint(11, 22, 0) });   // moved in Edit

            Assert.True(t.CanTranslate(repo, e));
            var saved = t.Extract(repo, e, null!);
            Assert.Equal(0.5, ((JsonValue)saved["Presence"]).GetValue<double>());
            var places = (JsonArray)saved["Positions"];
            Assert.Equal(2, places.Count);
            Assert.Equal("[11,22,0]", places[0]!.ToJsonString());                     // the CHOSEN (variant 0 ⇒ first) place follows the move
            Assert.Equal("[30,40,0]", places[1]!["at"]!.ToJsonString());              // the others as authored
            Assert.Equal(90, places[1]!["heading"]!.GetValue<int>());
            Assert.False(saved.ContainsKey(VariantForms.ComponentName));               // the holder itself is never saved
        }

        [Fact]
        public void AOneOfNMacro_IsSavedAsItsList_WithTheChosenEntryReplacedByTheCurrentMacro()
        {
            var dom = Prepared(0);
            using var repo = new EntityRepository();
            var e = repo.CreateEntity();
            var data = Data(Entity(dom, "cccc0000-0000-4000-8000-000000000004"));
            new VariantFormsTranslator().Inject(repo, e, data, null!);
            var mt = new MacroInstanceTranslator();
            mt.Inject(repo, e, data, null!);
            Assert.Equal("forces/A", ((ISimulationView)repo).GetManagedComponentRO<MacroInstance>(e).Macro);
            repo.SetManagedComponent(e, new MacroInstance { Macro = "forces/C", ArgsJson = "{}" });   // "Change…" in Edit
            var saved = (JsonObject)mt.Extract(repo, e, null!)[MacroInstance.ComponentName];
            Assert.Equal("""["forces/C",{"macro":"forces/B","weight":3}]""", saved["Macro"]!.ToJsonString());
        }

        [Fact]
        public void AVariantWorld_IsNotSaveable_AndTheGateNamesTheVariant_AnAuthoredOneIs()
        {
            foreach (var (variant, saveable) in new[] { (0, true), (3, false) })
            {
                var dom = Prepared(variant);
                using var repo = new EntityRepository();
                var t = new VariantFormsTranslator();
                foreach (var (_, n) in (JsonObject)dom["entities"]!)
                {
                    var e = repo.CreateEntity();
                    t.Inject(repo, e, Data((JsonObject)n!), null!);
                }
                var why = ScenarioSaveGate.CanSave(repo);
                if (saveable) Assert.Null(why);
                else Assert.Contains("variant 3", why);
            }
            using var empty = new EntityRepository();
            Assert.Null(ScenarioSaveGate.CanSave(empty));   // nothing built from variant forms ⇒ saveable
        }
    }
}

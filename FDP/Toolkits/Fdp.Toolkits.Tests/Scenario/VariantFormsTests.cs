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

        // ── ⭐ CE-4326 (Presets §9a, gate P1) — a Preview's preset applied IN PLACE = the load with that preset ──────────────────────────────

        /// <summary>The world a load builds from <see cref="Prepared"/>(<paramref name="variant"/>): each surviving entity with its GUID, place, macro and forms.</summary>
        private static EntityRepository World(int variant)
        {
            var dom = Prepared(variant);
            var repo = new EntityRepository();
            repo.RegisterComponent<EntityGuid>();
            repo.RegisterComponent<SimTransform>();
            var forms = new VariantFormsTranslator();
            var macro = new MacroInstanceTranslator();
            foreach (var (key, n) in (JsonObject)dom["entities"]!)
            {
                var data = Data((JsonObject)n!);
                var e = repo.CreateEntity();
                repo.AddComponent(e, new EntityGuid(System.Guid.Parse(key)));
                forms.Inject(repo, e, data, null!);
                if (data.ContainsKey(MacroInstance.ComponentName)) macro.Inject(repo, e, data, null!);
                if (((JsonObject)n!)["SimTransform"] is JsonObject st)
                {
                    var at = (JsonArray)st["Position"]!;
                    var tr = new SimTransform { Position = new WorldPoint(F(at[0]), F(at[1]), F(at[2])), Rotation = new WorldRotation(0, 0, 0, 1) };
                    if (st["Rotation"] is JsonArray q) tr.Rotation = new WorldRotation(F(q[0]), F(q[1]), F(q[2]), F(q[3]));
                    repo.AddComponent(e, tr);
                }
            }
            return repo;
            static float F(JsonNode? n) => float.Parse(n!.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);   // the writer stores double, float or int
        }

        /// <summary>One line per entity — GUID, place, heading, macro, the forms' choices — so two worlds compare as text.</summary>
        private static string Fingerprint(EntityRepository repo)
        {
            var rows = new List<string>();
            var view = (ISimulationView)repo;
            foreach (var e in repo.Query().With<EntityGuid>().Build())
            {
                string place = repo.HasComponent<SimTransform>(e) ? $"{repo.GetComponentRO<SimTransform>(e).Position} {(System.Numerics.Quaternion)repo.GetComponentRO<SimTransform>(e).Rotation}" : "-";
                string mac = repo.HasManagedComponent<MacroInstance>(e) ? view.GetManagedComponentRO<MacroInstance>(e).Macro : "-";
                var f = repo.HasManagedComponent<VariantForms>(e) ? view.GetManagedComponentRO<VariantForms>(e) : null;
                rows.Add($"{repo.GetComponentRO<EntityGuid>(e).Value} | {place} | {mac} | {f?.Variant}/{f?.PositionChosen}/{f?.MacroChosen}");
            }
            rows.Sort(System.StringComparer.Ordinal);
            return string.Join("\n", rows);
        }

        /// <summary>
        /// ⭐⭐ CE-4326 gate P1 — for 200 variants, the authored world with the preset applied IN PLACE (<see cref="PresetInPlace.Apply"/>) equals the world a load
        /// WITH that preset builds: the same entities present, the same places and headings, the same macros, the same recorded choices — so the two writers share
        /// one set of decisions. 🔴 Red-proof (compiles): <c>PresetInPlace</c> rolling presence with <c>"presence "</c> (another key) ⇒ some variant differs ⇒ red.
        /// </summary>
        [Fact]
        public void CE4326_APresetAppliedInPlace_EqualsTheLoadWithThatPreset_ForEveryVariant()
        {
            int removed = 0, kept = 0;
            for (int v = 1; v <= 200; v++)
            {
                using var loaded = World(v);
                using var inPlace = World(0);
                var result = PresetInPlace.Apply(inPlace, "{\"$variant\": " + v + "}");
                Assert.Null(result.Refused);
                Assert.Equal(Fingerprint(loaded), Fingerprint(inPlace));
                if (result.Removed > 0) removed++; else kept++;
            }
            Assert.True(removed > 0 && kept > 0, $"the presence roll went both ways ({removed} removed, {kept} kept)");   // the rail exercised both branches
        }

        /// <summary>⭐ CE-4326 — no setup (Default) changes nothing; an undeclared param is refused and changes nothing.</summary>
        [Fact]
        public void CE4326_DefaultChangesNothing_AnUndeclaredArgIsRefusedAndChangesNothing()
        {
            using var w = World(0);
            var before = Fingerprint(w);
            Assert.Equal(default, PresetInPlace.Apply(w, null));
            var refused = PresetInPlace.Apply(w, "{\"speed\": 3}");
            Assert.Contains("speed", refused.Refused);
            Assert.Equal(before, Fingerprint(w));
        }

        /// <summary>
        /// ⭐ CE-4326 (P3) — the authored forms survive a dry run's keyframe round trip on BOTH dry-run capture sets (a node's and the editor's): an entity a
        /// Preview removed comes back with its <see cref="VariantForms"/> WHOLE (the nullable <c>Presence</c>, the JSON lists, the choices) — else a save after
        /// the Preview would write the scenario without them.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CE4326_VariantForms_SurviveTheDryRunKeyframe(bool editor)
        {
            using var repo = new EntityRepository();
            repo.RegisterManagedComponent<VariantForms>();
            var e = repo.CreateEntity();
            var authored = new VariantForms { Variant = 0, Presence = 0.5, PositionsJson = "[[1,2,0]]", PositionChosen = 0, MacroChoicesJson = "[\"a\",\"b\"]", MacroChosen = 1 };
            repo.SetManagedComponent(e, authored);
            var key = Fdp.Toolkit.Replay.WorldKeyframes.Capture(repo, editor ? Fdp.Toolkit.Replay.CaptureOptions.EditorDryRun : Fdp.Toolkit.Replay.CaptureOptions.NodeDryRun);
            repo.DestroyEntity(e);
            Fdp.Toolkit.Replay.WorldKeyframes.Apply(repo, key);
            VariantForms? back = null;
            foreach (var x in repo.Query().WithManaged<VariantForms>().Build()) back = ((ISimulationView)repo).GetManagedComponentRO<VariantForms>(x);
            Assert.Equal(authored, back);
        }

        // ── ⭐ CE-4326 part ② (Presets §9a.2) — a preset's PARAMS applied in place through the host's serializer ─────────────────────────────

        /// <summary>A bound scalar (the test's own component, so the rail owns its shape).</summary>
        [ComponentId(492)]   // test-local (GlobalComponentIds notes it as taken)
        public struct PresetSpeed { public float Speed; public int Lane; }

        private const string BoundFile = """
            { "Header": { "SubsystemType": "Hrot.Scenario", "Params": [ { "Name": "speed", "TypeId": "float", "Default": 1.5 } ] },
              "Entities": {
                "dddd0000-0000-4000-8000-000000000001": { "PresetSpeed": {"Speed": 0, "Lane": 7},
                    "ParamBindings": {"Bindings": {"PresetSpeed.Speed": "speed"}} },
                "dddd0000-0000-4000-8000-000000000002": { "PresetSpeed": {"Speed": 9, "Lane": 3} } } }
            """;

        private static ScenarioSerializer BoundSerializer()
            => new ScenarioSerializerBuilder("Hrot.Scenario").RegisterTranslator(new ParamBindingsTranslator()).Build();

        /// <summary>The world a load WITH <paramref name="args"/> builds: the file prepared by the load's own routine, then deserialized.</summary>
        private static EntityRepository BoundWorld(string? args)
        {
            var dom = (JsonObject)JsonNode.Parse(BoundFile)!;
            var header = ScenarioHeaderJson.Read(dom);
            var prepared = ScenarioParams.Prepare(dom, header, args, null, "scenario 'bound'");
            var repo = new EntityRepository();
            repo.RegisterComponent<EntityGuid>();
            repo.RegisterComponent<PresetSpeed>();
            repo.RegisterManagedComponent<ParamBindings>();
            repo.RegisterManagedComponent<ScenarioDescriptionResident>();
            BoundSerializer().Deserialize(repo, dom);
            ScenarioDescription.Stamp(repo, null, "bound", null, prepared.EffectiveJson, ScenarioDescription.HeaderJsonOf(header));
            return repo;
        }

        private static string Speeds(EntityRepository repo)
        {
            var rows = new List<string>();
            foreach (var e in repo.Query().With<EntityGuid>().Build())
            {
                var s = repo.GetComponentRO<PresetSpeed>(e);
                rows.Add($"{repo.GetComponentRO<EntityGuid>(e).Value} {s.Speed} {s.Lane} pb={repo.HasManagedComponent<ParamBindings>(e)}");
            }
            rows.Sort(System.StringComparer.Ordinal);
            return string.Join("\n", rows);
        }

        /// <summary>
        /// ⭐⭐ CE-4326 part ② gate P1 — a pen preset's PARAMS applied in place (the authored world, loaded with the defaults) equal the load WITH that preset:
        /// the bound field takes the preset's value, the unbound field and the unbound entity keep theirs, the binding stays (SR-168). And without the host's
        /// serializer nothing is bound — counted, never silent.
        /// 🔴 Red-proof (compiles): <c>ApplyParams</c> skipping the inject (<c>continue</c> before <c>DeserializeWith</c>) ⇒ the speed stays 1.5 ⇒ red.
        /// </summary>
        [Fact]
        public void CE4326_APresetsParams_AppliedInPlace_EqualTheLoadWithThatPreset()
        {
            const string preset = "{\"speed\": 4.25}";
            using var loaded = BoundWorld(preset);
            using var inPlace = BoundWorld(null);
            Assert.Contains(" 1.5 7 ", Speeds(inPlace));   // the authored world holds the default

            var result = PresetInPlace.Apply(inPlace, preset, BoundSerializer());
            Assert.Null(result.Refused);
            Assert.Equal(1, result.Bound);
            Assert.Equal(0, result.Unbound);
            Assert.Equal(Speeds(loaded), Speeds(inPlace));
            Assert.Contains(" 4.25 7 pb=True", Speeds(inPlace));

            using var noHost = BoundWorld(null);
            var bare = PresetInPlace.Apply(noHost, preset);
            Assert.Equal((0, 1), (bare.Bound, bare.Unbound));
            Assert.Contains(" 1.5 7 ", Speeds(noHost));
        }
    }
}

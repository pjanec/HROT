using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐⭐ Macros S3a ① (<c>docs/DESIGN_Scenario_Macros.md</c> §5.1 SR-159…SR-163, §5.1.2 SR-164, §14 SR-168) — the ONE param binder:
    /// ① a <c>"^param"</c> reference / place / area takes the param's value and KEEPS its form; ② a <see cref="BindableNode"/>'s
    /// <c>Bind</c> sets the node's own properties at any depth of a predicate tree (object and JSON); ③ <see cref="ParamBindings"/> sets
    /// an entity's scalar fields by path, into a params blob too. Every binding survives the value's own JSON round trip (SR-163/SR-168).
    /// </summary>
    public sealed class ParamBinderTests
    {
        private static readonly JsonSerializerOptions Json = FdpJsonOptionsRegistry.DefaultRelaxed;

        public sealed class Params
        {
            public EntityLink Subject;
            public PointLink At;
            public AreaLink Zone;
            public float Speed;
        }

        // the shape of a trigger record: a predicate tree under a typed root
        public sealed class Holder { public SearchPredicateDto? When { get; set; } }

        private sealed class Source : IParamSource
        {
            public readonly Dictionary<string, object?> Values = new(StringComparer.OrdinalIgnoreCase);
            public string Describe => "test macro";
            public bool TryGet(string name, Type target, out object? value)
            {
                if (!Values.TryGetValue(name, out value)) return false;
                if (target == typeof(JsonNode)) value = JsonSerializer.SerializeToNode(value, Json);
                else if (value is not null && !target.IsInstanceOfType(value)) value = Convert.ChangeType(value, target);
                return true;
            }
        }

        private sealed class Scope : IRefScope
        {
            public string Describe => "outside";
            public bool TryResolve(in RefSpec spec, out EntityRef entity)
            { entity = spec.KeyText == "Village" ? new EntityRef(77) : EntityRef.None; return !entity.IsNone; }
            public bool TryPositionOf(EntityRef entity, out WorldPoint position) { position = new WorldPoint(5, 6, 0); return true; }
        }

        // ── the Param form ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void TheParamForm_ParsesAndPrints_AndRefSpecStaysThirtySixBytes()
        {
            Assert.True(RefSpec.TryParse("^area", out var s));
            Assert.Equal(RefForm.Param, s.Form);
            Assert.Equal("area", s.ParamName);
            Assert.Equal("^area", s.ToString());
            Assert.Equal(RefForm.None, s.Bound);
            Assert.Equal(36, Unsafe.SizeOf<RefSpec>());   // Bound sits in byte 33 — the saved / recorded layout is unchanged
            Assert.NotEqual(s, s.WithBound(RefForm.Circle));
            Assert.Throws<InvalidOperationException>(() => RefSpec.ByKey("x").WithBound(RefForm.Key));
        }

        // ── ① references, places, areas ───────────────────────────────────────────────────────────────

        [Fact]
        public void One_AParamLeaf_TakesTheValue_AndKeepsTheBinding_ThroughItsJson()
        {
            var p = JsonSerializer.Deserialize<Params>("""{"Subject": "^subject", "At": "^at", "Zone": "^zone", "Speed": 2}""", Json)!;
            var src = new Source();
            src.Values["subject"] = new EntityLink(new EntityRef(42), RefSpec.ByKey("BlueHQ"));
            src.Values["at"] = new PointLink(new WorldPoint(10, 20, 3));
            src.Values["zone"] = AreaLink.OfEntity(new EntityRef(9), RefSpec.ByKey("Village"));

            Assert.True(ParamBinder.Apply(p, src));

            Assert.Equal(42, p.Subject.Ref.NetworkId);
            Assert.Equal(RefForm.Param, p.Subject.Spec.Form);              // SR-168: the binding is KEPT
            Assert.Equal(RefForm.Key, p.Subject.Spec.Bound);
            Assert.Equal(new WorldPoint(10, 20, 3), p.At.Local);
            Assert.Equal(9, p.Zone.Area.NetworkId);
            Assert.False(p.Zone.IsShape);

            // a save writes the live value PLUS the binding; the next load reads the binding back
            var json = JsonSerializer.Serialize(p, Json);
            Assert.Contains("\"^subject\"", json);
            Assert.Contains("\"^at\"", json);
            Assert.Contains("\"^zone\"", json);
            var back = JsonSerializer.Deserialize<Params>(json, Json)!;
            Assert.Equal(RefForm.Param, back.Subject.Spec.Form);
            Assert.Equal(RefForm.Param, back.At.Spec.Form);
            Assert.Equal(RefForm.Param, back.Zone.Spec.Form);
        }

        [Fact]
        public void One_AnAreaParamBoundToACircle_ActsAsTheCircle_RoundTrips_AndIsNotRelocated()
        {
            var p = JsonSerializer.Deserialize<Params>("""{"Zone": "^zone", "At": "^at"}""", Json)!;
            var src = new Source();
            src.Values["zone"] = AreaLink.Circle(new WorldPoint(100, 0, 0), 10);
            src.Values["at"] = new PointLink(new WorldPoint(7, 8, 0));
            ParamBinder.Apply(p, src);

            Assert.True(p.Zone.IsShape);
            Assert.True(p.Zone.Contains(null, new Vector3(105, 0, 0)));
            Assert.False(p.Zone.Contains(null, new Vector3(115, 0, 0)));

            var back = JsonSerializer.Deserialize<Params>(JsonSerializer.Serialize(p, Json), Json)!;
            Assert.Equal(p.Zone, back.Zone);                               // {"circle": …, "Ref": "^zone"}

            // SR-131: a value that came in through a param belongs to the world outside the macro — relocation leaves it
            var turned = new Relocation(WorldPoint.Zero, default, new WorldPoint(1000, 0, 0), new Bearing(90));
            Assert.Equal(p.Zone, turned.Area(p.Zone));
            Assert.Equal(p.At, turned.PointLink(p.At));
        }

        [Fact]
        public void One_AMissingParam_IsLoud_NamingIt()
        {
            var p = JsonSerializer.Deserialize<Params>("""{"Subject": "^nobody"}""", Json)!;
            var ex = Assert.Throws<InvalidOperationException>(() => ParamBinder.Apply(p, new Source()));
            Assert.Contains("no param 'nobody'", ex.Message);
        }

        [Fact]
        public void One_AParamsBlob_IsBoundInPlace_AndAnUnboundBlobComesBackAsTheSameString()
        {
            var src = new Source();
            src.Values["at"] = new PointLink(new WorldPoint(1, 2, 3));
            const string unbound = """{"At": [1, 1, 1], "Speed": 2}""";
            Assert.Same(unbound, ParamBinder.ApplyJson(typeof(Params), unbound, src));

            var bound = ParamBinder.ApplyJson(typeof(Params), """{"At": "^at", "Speed": 2}""", src)!;
            var p = JsonSerializer.Deserialize<Params>(bound, Json)!;
            Assert.Equal(new WorldPoint(1, 2, 3), p.At.Local);
            Assert.Equal(RefForm.Param, p.At.Spec.Form);
        }

        // ── ② Bind on a node, at any depth of a predicate tree ────────────────────────────────────────

        private static Holder Tree() => new()
        {
            When = new CompoundPredicateDto
            {
                Conditions =
                {
                    new NumericPredicateDto { MinValue = 1, Bind = new() { ["MinValue"] = "minDelay" } },
                    new CompoundPredicateDto { Conditions = { new NumericPredicateDto { MaxValue = 9, Bind = new() { ["maxValue"] = "cap" } } } },
                },
            },
        };

        [Fact]
        public void Two_BindOnANode_SetsItsProperty_AtAnyDepth_AndTheBindStays()
        {
            var h = Tree();
            var src = new Source();
            src.Values["minDelay"] = 30.0;
            src.Values["cap"] = 45.0;

            Assert.True(ParamBinder.Apply(h, src));

            var root = (CompoundPredicateDto)h.When!;
            var first = (NumericPredicateDto)root.Conditions[0];
            var deep = (NumericPredicateDto)((CompoundPredicateDto)root.Conditions[1]).Conditions[0];
            Assert.Equal(30.0, first.MinValue);
            Assert.Equal(45.0, deep.MaxValue);
            Assert.Equal("minDelay", first.Bind!["MinValue"]);              // SR-168: kept
        }

        [Fact]
        public void Two_BindInJson_IsAppliedThroughTheDiscriminator_AndRoundTrips()
        {
            var json = JsonSerializer.Serialize(Tree(), Json);
            Assert.Contains("\"Bind\"", json);
            var src = new Source();
            src.Values["minDelay"] = 30.0;
            src.Values["cap"] = 45.0;

            var bound = ParamBinder.ApplyJson(typeof(Holder), json, src)!;
            var h = JsonSerializer.Deserialize<Holder>(bound, Json)!;
            var root = (CompoundPredicateDto)h.When!;
            Assert.Equal(30.0, ((NumericPredicateDto)root.Conditions[0]).MinValue);
            Assert.Equal(45.0, ((NumericPredicateDto)((CompoundPredicateDto)root.Conditions[1]).Conditions[0]).MaxValue);
            Assert.Equal("cap", ((CompoundPredicateDto)root.Conditions[1]).Conditions[0].Bind!["maxValue"]);
        }

        [Fact]
        public void Two_APredicateWithNoBind_WritesNoBindKey_AndANonPropertyIsLoud()
        {
            Assert.DoesNotContain("Bind", JsonSerializer.Serialize<SearchPredicateDto>(new NumericPredicateDto(), Json));
            var h = new Holder { When = new NumericPredicateDto { Bind = new() { ["Nope"] = "x" } } };
            var src = new Source();
            src.Values["x"] = 1.0;
            var ex = Assert.Throws<InvalidOperationException>(() => ParamBinder.Apply(h, src));
            Assert.Contains("no settable property 'Nope'", ex.Message);
        }

        // ── ③ ParamBindings — an entity's scalar fields by path ───────────────────────────────────────

        private static JsonObject Entity() => JsonNode.Parse("""
            {
              "EntityInfo": { "Name": "QRF lead", "ForceId": 1 },
              "Behavior":   { "Name": "ClearArea", "Params": "{\"Area\":\"^area\",\"Speed\":4.0}" },
              "ParamBindings": { "Bindings": { "Behavior.Params.Speed": "speed", "EntityInfo.ForceId": "side" } }
            }
            """)!.AsObject();

        [Fact]
        public void Three_ParamBindings_SetFieldsByPath_IntoAParamsBlobToo_AndStayOnTheEntity()
        {
            var e = Entity();
            var src = new Source();
            src.Values["speed"] = 7.5;
            src.Values["side"] = 2;

            Assert.True(ParamBinder.ApplyBindings(e, src));

            Assert.Equal(2, e["EntityInfo"]!["ForceId"]!.GetValue<int>());
            var blob = JsonNode.Parse(Fdp.Core.Serialization.Converters.JsonTextConverter.TextOf(e["Behavior"]!["Params"])!)!;
            Assert.Equal(7.5, blob["Speed"]!.GetValue<double>());
            Assert.Equal("^area", blob["Area"]!.GetValue<string>());       // the ① binding beside it is untouched
            Assert.NotNull(e["ParamBindings"]);                             // SR-168
            Assert.Equal(2, ParamBinder.BindingsOf(e).Count);
            Assert.False(ParamBinder.ApplyBindings(e, src));                // applying again changes nothing
        }

        [Theory]
        [InlineData("Behavior.Params.Route[0]", "an index in a path is refused")]
        [InlineData("Weapon.Ammo", "no component 'Weapon'")]
        [InlineData("EntityInfo.Name.First", "is not an object")]
        public void Three_ABadPath_IsLoud(string path, string expected)
        {
            var e = Entity();
            e["ParamBindings"]!["Bindings"] = new JsonObject { [path] = "speed" };
            var src = new Source();
            src.Values["speed"] = 1.0;
            var ex = Assert.Throws<InvalidOperationException>(() => ParamBinder.ApplyBindings(e, src));
            Assert.Contains(expected, ex.Message);
        }

        /// <summary>⭐ R-3 (AR-5) — the ONE write walker: keys ignore case, a string-held object is walked and written back, the last key is created, and the binder still refuses an index.</summary>
        [Fact]
        public void ComponentJsonPathSet_KeepsTheBindersWalkRules()
        {
            var root = JsonNode.Parse("""{"Behavior":{"Params":"{\"Speed\":1}"},"EntityInfo":{"ForceId":1}}""")!.AsObject();
            Assert.Equal(ComponentJsonPath.SetOutcome.Changed, ComponentJsonPath.Set(root, "entityinfo.FORCEID", JsonValue.Create(2), out _));
            Assert.Equal(2, root["EntityInfo"]!["ForceId"]!.GetValue<int>());                          // another case hit the existing key, no twin
            Assert.Single(root["EntityInfo"]!.AsObject());
            Assert.Equal(ComponentJsonPath.SetOutcome.Changed, ComponentJsonPath.Set(root, "Behavior.Params.speed", JsonValue.Create(7.5), out _));
            Assert.Equal(7.5, JsonNode.Parse(root["Behavior"]!["Params"]!.GetValue<string>())!["Speed"]!.GetValue<double>());   // the string was walked and written back
            Assert.Equal(ComponentJsonPath.SetOutcome.Unchanged, ComponentJsonPath.Set(root, "Behavior.Params.Speed", JsonValue.Create(7.5), out _));
            Assert.Equal(ComponentJsonPath.SetOutcome.MissingStep, ComponentJsonPath.Set(root, "Weapon.Ammo", JsonValue.Create(1), out var bad));
            Assert.Equal(0, bad);
            Assert.Equal(ComponentJsonPath.SetOutcome.NotAnObject, ComponentJsonPath.Set(root, "EntityInfo.ForceId.X", JsonValue.Create(1), out bad));
            Assert.Equal(1, bad);

            var e = Entity();                                                                            // the binder's own check stays in front of Set
            e["ParamBindings"]!["Bindings"] = new JsonObject { ["Behavior.Params.Route[0]"] = "speed" };
            var src = new Source(); src.Values["speed"] = 1.0;
            Assert.Contains("an index in a path is refused", Assert.Throws<InvalidOperationException>(() => ParamBinder.ApplyBindings(e, src)).Message);
        }

        [Fact]
        public void Three_AMissingParam_IsLoud_NamingThePath()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ParamBinder.ApplyBindings(Entity(), new Source()));
            Assert.Contains("no param", ex.Message);
        }

        // ── the JSON source: defaults ▸ args, references resolved OUTSIDE ─────────────────────────────

        [Fact]
        public void TheJsonSource_OverlaysArgsOnDefaults_AndResolvesAReferenceOutside()
        {
            var values = JsonParamSource.Overlay(
                new Dictionary<string, JsonNode?> { ["area"] = "@Village", ["speed"] = 4.0 },
                new Dictionary<string, JsonNode?> { ["SPEED"] = 9.0 });
            var src = new JsonParamSource(values, new RefResolve(new Scope()), "macro 'test'");

            Assert.True(src.TryGet("speed", typeof(float), out var speed));
            Assert.Equal(9f, speed);
            Assert.True(src.TryGet("area", typeof(EntityLink), out var area));
            Assert.Equal(77, ((EntityLink)area!).Ref.NetworkId);
            Assert.False(src.TryGet("absent", typeof(float), out _));
            var ex = Assert.Throws<InvalidOperationException>(() => src.TryGet("area", typeof(float), out _));
            Assert.Contains("the param 'area'", ex.Message);
        }

        // ── the component ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void ParamBindings_IsNoReplay_AndItsTranslatorRoundTrips()
        {
            var policy = (DataPolicyAttribute?)Attribute.GetCustomAttribute(typeof(ParamBindings), typeof(DataPolicyAttribute));
            Assert.NotNull(policy);
            Assert.True(policy!.Policy.HasFlag(DataPolicy.NoReplay));

            using var repo = new EntityRepository();
            repo.RegisterManagedComponent<ParamBindings>();
            var e = repo.CreateEntity();
            repo.SetManagedComponent(e, new ParamBindings { Bindings = new Dictionary<string, string> { ["EntityInfo.ForceId"] = "side" } });

            var t = new ParamBindingsTranslator();
            Assert.True(t.CanTranslate(repo, e));
            var saved = t.Extract(repo, e, null!);
            var e2 = repo.CreateEntity();
            t.Inject(repo, e2, saved, null!);
            Assert.Equal("side", ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetManagedComponentRO<ParamBindings>(e2)!.Bindings["EntityInfo.ForceId"]);
        }
    }
}

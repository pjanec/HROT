using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐⭐ Macros S3a ② + ③ (<c>docs/DESIGN_Scenario_Macros.md</c> §10.3, §14 SR-152 / SR-156 / SR-170 / SR-173 / SR-175 / SR-178 / SR-182, §14.1 ①):
    /// the macro FILE (its header fields round-trip; it opens by its NAME through the one resolver) and the three component types (policies,
    /// immutability, the instance's scenario translator).
    /// </summary>
    public sealed class MacroTypesTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "macros"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        // ── ② the macro file ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void TheMacroHeader_EveryField_RoundTripsThroughSerializeAndRead()
        {
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();
            var header = new ScenarioHeader("Hrot.Scenario", TerrainName: "test-town", Description: "What: x · Judge: y")
            {
                Kind = ScenarioHeader.MacroKind,
                Params = new[]
                {
                    new ScenarioParamDecl("side", "ForceId", JsonValue.Create("Hostile"), "which force"),
                    new ScenarioParamDecl("count", "int", JsonValue.Create(4)),
                },
                Anchor = new MacroAnchor(new WorldPoint(100, 200, 3), new Bearing(45)),
                MaxShift = 12.5f,
                Relocatable = false,
            };
            var dom = new ScenarioSerializerBuilder("Hrot.Scenario").Build().Serialize(repo, header);

            var back = ScenarioHeaderJson.Read(dom)!;
            Assert.True(back.IsMacro);
            Assert.Equal("test-town", back.TerrainName);
            Assert.Equal("What: x · Judge: y", back.Description);
            Assert.Equal(2, back.Params!.Count);
            Assert.Equal("side", back.Params[0].Name);
            Assert.Equal("ForceId", back.Params[0].TypeId);
            Assert.Equal("Hostile", back.Params[0].Default!.GetValue<string>());
            Assert.Equal("which force", back.Params[0].Comment);
            Assert.Equal(4, back.Params[1].Default!.GetValue<int>());
            Assert.Equal(new WorldPoint(100, 200, 3), back.Anchor!.Value.Position);
            Assert.Equal(45f, back.Anchor.Value.Heading.Degrees);
            Assert.Equal(12.5f, back.MaxShift);
            Assert.Equal(12.5f, back.EffectiveMaxShift);
            Assert.False(back.Relocatable);
        }

        [Fact]
        public void AScenarioHeader_WritesNoMacroFields_AndAMacroWithoutPlacementGetsTheDefaultShift()
        {
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();
            var plain = new ScenarioSerializerBuilder("Hrot.Scenario").Build().Serialize(repo, new ScenarioHeader("Hrot.Scenario", TerrainName: "t"));
            var h = plain["Header"]!.AsObject();
            Assert.False(h.ContainsKey("Kind"));
            Assert.False(h.ContainsKey("Params"));
            Assert.False(h.ContainsKey("Anchor"));
            Assert.False(h.ContainsKey("Placement"));
            Assert.False(h.ContainsKey("Relocatable"));   // ⚠ only when false

            var back = ScenarioHeaderJson.Read(plain)!;
            Assert.False(back.IsMacro);
            Assert.True(back.Relocatable);
            Assert.Null(back.MaxShift);
            Assert.Equal(ScenarioHeader.DefaultMaxShift, back.EffectiveMaxShift);
            Assert.Equal(30f, ScenarioHeader.DefaultMaxShift);
        }

        [Fact]
        public void TheSampleMacroFile_OpensByItsName_AndItsHeaderReads()
        {
            var root = RepoRoot();
            var dir = ContentLayout.ResolveScenarioDirectory(root, "macros/test/fire-team");
            Assert.EndsWith(Path.Combine("macros", "test", "fire-team"), dir);
            var file = Path.Combine(dir, ContentLayout.MacroFileName);
            Assert.True(File.Exists(file), file);

            var dom = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
            var h = ScenarioHeaderJson.Read(dom)!;   // ⚠ real files carry the lower-case "header" / "entities"
            Assert.True(h.IsMacro);
            Assert.Equal("side", h.Params!.Single().Name);
            Assert.Equal("ForceId", h.Params.Single().TypeId);
            Assert.Equal("Hostile", h.Params.Single().Default!.GetValue<string>());
            Assert.NotNull(h.Anchor);
            Assert.Equal(0f, h.Anchor!.Value.Heading.Degrees);
            Assert.InRange(((JsonObject)dom["entities"]!).Count, 3, 4);
        }

        // ── ③ the types ──────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void ThePolicies_AreSavedForTheInstance_AndNeverForTheRuntimeStateOrTheOrigin()
        {
            static DataPolicy PolicyOf(Type t) => t.GetCustomAttribute<DataPolicyAttribute>()!.Policy;
            Assert.Equal(DataPolicy.Default, PolicyOf(typeof(MacroInstance)));
            Assert.True(PolicyOf(typeof(MacroExpansionStatus)).HasFlag(DataPolicy.NoScenario));
            Assert.True(PolicyOf(typeof(MacroOrigin)).HasFlag(DataPolicy.NoScenario));
            Assert.False(PolicyOf(typeof(MacroExpansionStatus)).HasFlag(DataPolicy.NoReplay));   // recorded
            Assert.False(PolicyOf(typeof(MacroOrigin)).HasFlag(DataPolicy.NoReplay));
            Assert.Equal(470, typeof(MacroInstance).GetCustomAttribute<ComponentIdAttribute>()!.Id);
            Assert.Equal(471, typeof(MacroExpansionStatus).GetCustomAttribute<ComponentIdAttribute>()!.Id);
            Assert.Equal(472, typeof(MacroOrigin).GetCustomAttribute<ComponentIdAttribute>()!.Id);
        }

        /// <summary>🔴 Mutation: a <c>set</c> instead of <c>init</c> on either property ⇒ red (CLAUDE.md "NEW COMPONENTS": a managed component is IMMUTABLE).</summary>
        [Fact]
        public void TheManagedInstance_IsImmutable_EveryPropertyIsInitOnly()
        {
            foreach (var p in typeof(MacroInstance).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var setter = p.SetMethod;
                Assert.NotNull(setter);
                Assert.Contains(setter!.ReturnParameter.GetRequiredCustomModifiers(), m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
            }
            Assert.Equal("{}", new MacroInstance().ArgsJson);
            Assert.True(typeof(MacroInstance).IsSealed);
        }

        [Fact]
        public void TheUnmanagedTypes_AreUnmanagedStructs()
        {
            Assert.True(typeof(MacroExpansionStatus).IsValueType && !System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<MacroExpansionStatus>());
            Assert.True(typeof(MacroOrigin).IsValueType && !System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<MacroOrigin>());
        }

        [Fact]
        public void TheInstanceTranslator_RoundTrips_MacroAndArgsAsARealJsonObject()
        {
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();
            repo.RegisterManagedComponent<MacroInstance>();
            var e = repo.CreateEntity();
            repo.SetManagedComponent(e, new MacroInstance { Macro = "test/fire-team", ArgsJson = "{\"side\":\"Friend\",\"n\":3}" });

            var tr = new MacroInstanceTranslator();
            Assert.True(tr.CanTranslate(repo, e));
            var data = tr.Extract(repo, e, null!);
            var node = (JsonObject)data["MacroInstance"];
            Assert.Equal("test/fire-team", node["Macro"]!.GetValue<string>());
            Assert.IsType<JsonObject>(node["Args"]);                       // an object, not a string
            Assert.Equal("Friend", node["Args"]!["side"]!.GetValue<string>());

            var f = repo.CreateEntity();
            tr.Inject(repo, f, data, null!);
            var back = ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetManagedComponentRO<MacroInstance>(f);
            Assert.Equal("test/fire-team", back.Macro);
            Assert.Equal("Friend", JsonNode.Parse(back.ArgsJson)!["side"]!.GetValue<string>());
            Assert.Equal(3, JsonNode.Parse(back.ArgsJson)!["n"]!.GetValue<int>());
        }
    }
}

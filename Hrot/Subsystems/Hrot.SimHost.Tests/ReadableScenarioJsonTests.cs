using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Converters;
using Fdp.Testing;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4295</c> (R-4159) — a behaviour's / task's / trigger's params are written to a scenario as the JSON object they hold, read
    /// from either form, and give back the SAME in-memory string. 📄 <c>docs/DESIGN_Readable_Scenario_Json.md</c> §2. One round-trip rail
    /// per marked field, through both option sets the scenario uses; old file vs new file through the PRODUCTION translators; and the
    /// shipped corpus carries no escaped params (a generator that regresses is caught here).
    /// </summary>
    public sealed class ReadableScenarioJsonTests
    {
        private const string Params = "{\"Building\": [47, 105, 0], \"Speed\": 2.0}";

        public static IEnumerable<object[]> Options()
        {
            yield return new object[] { "DefaultRelaxed" };
            yield return new object[] { "HrotJsonOptions" };
        }

        private static JsonSerializerOptions Of(string name)
            => name == "DefaultRelaxed" ? FdpJsonOptionsRegistry.DefaultRelaxed : HrotSerializerOptions.HrotJsonOptions;

        // ── one round trip per marked field: string → object → the SAME string; the old (string) form reads the same ──────────────

        [Theory, MemberData(nameof(Options))]
        public void SavedBrainSlot_Params_IsWrittenAsAnObject_AndReadsBackTheSameText(string options)
        {
            var o = Of(options);
            string json = JsonSerializer.Serialize(new SavedBrainSlot { Name = "ClearBuilding", Params = Params }, o);
            Assert.IsType<JsonObject>(PropertyOf(JsonNode.Parse(json)!, "Params"));
            Assert.Equal(Params, JsonSerializer.Deserialize<SavedBrainSlot>(json, o)!.Params);
            string old = "{\"Name\":\"ClearBuilding\",\"Params\":" + JsonSerializer.Serialize(Params) + "}";
            Assert.Equal(Params, JsonSerializer.Deserialize<SavedBrainSlot>(old, o)!.Params);
        }

        [Theory, MemberData(nameof(Options))]
        public void DomainMissionTask_BehaviorParams_IsWrittenAsAnObject_AndReadsBackTheSameText(string options)
        {
            var o = Of(options);
            string json = JsonSerializer.Serialize(new DomainMissionTask { BehaviorName = "ClearBuilding", BehaviorParams = Params }, o);
            Assert.IsType<JsonObject>(PropertyOf(JsonNode.Parse(json)!, "BehaviorParams"));
            Assert.Equal(Params, JsonSerializer.Deserialize<DomainMissionTask>(json, o)!.BehaviorParams);
            string old = "{\"BehaviorName\":\"ClearBuilding\",\"BehaviorParams\":" + JsonSerializer.Serialize(Params) + "}";
            Assert.Equal(Params, JsonSerializer.Deserialize<DomainMissionTask>(old, o)!.BehaviorParams);
        }

        [Theory, MemberData(nameof(Options))]
        public void DomainMissionTrigger_Params_IsWrittenAsAnObject_AndReadsBackTheSameText(string options)
        {
            var o = Of(options);
            string json = JsonSerializer.Serialize(new DomainMissionTrigger { Type = "Condition", Params = Params }, o);
            Assert.IsType<JsonObject>(PropertyOf(JsonNode.Parse(json)!, "Params"));
            Assert.Equal(Params, JsonSerializer.Deserialize<DomainMissionTrigger>(json, o)!.Params);
        }

        /// <summary>A scalar (a trigger's <c>"40"</c>), an empty string and text that is not JSON stay STRINGS — never a changed JSON type,
        /// never a lost author's text.</summary>
        [Theory]
        [InlineData("40")]
        [InlineData("")]
        [InlineData("{not json")]
        [InlineData("true")]
        public void ANonStructuredValue_StaysAString(string text)
        {
            var o = HrotSerializerOptions.HrotJsonOptions;
            string json = JsonSerializer.Serialize(new DomainMissionTrigger { Type = "TimeElapsed", Params = text }, o);
            var node = PropertyOf(JsonNode.Parse(json)!, "Params");
            Assert.True(node is JsonValue v && v.TryGetValue<string>(out var s) && s == text, json);
            Assert.Equal(text, JsonSerializer.Deserialize<DomainMissionTrigger>(json, o)!.Params);
        }

        // ── old file → load → identical world, through the PRODUCTION translators ────────────────────────────────────────────────────

        [Fact]
        public void MissionPlan_AnOldFileAndANewFile_LoadTheSameParams()
        {
            var registry = new BehaviorRegistry();
            registry.Register(7, "ClearBuilding", new BehaviorDefinition { Name = "ClearBuilding", BrainTier = BehaviorConstants.BrainTierBTree });
            string Load(JsonNode paramsNode)
            {
                using var repo = new EntityRepository();
                repo.RegisterComponent<MissionPlanQueue>();
                repo.RegisterManagedComponent<ActiveMissionPlan>();
                var e = repo.CreateEntity();
                var plan = new JsonObject
                {
                    ["PlanData"] = new JsonObject
                    {
                        ["tasks"] = new JsonArray(new JsonObject
                        {
                            ["taskId"] = Guid.NewGuid().ToString("D"), ["behaviorName"] = "ClearBuilding", ["behaviorParams"] = paramsNode,
                        }),
                    },
                };
                new MissionPlanTranslator(registry).Inject(repo, e, new Dictionary<string, object> { ["MissionPlan"] = plan }, new NullGuidResolver());
                return ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(e).Plan.Tasks[0].BehaviorParams;
            }
            Assert.Equal(Params, Load(JsonValue.Create(Params)!));   // the old, escaped form
            Assert.Equal(Normalised(Params), Normalised(Load(JsonNode.Parse(Params)!)));   // the new form (a tree re-written: same JSON)
        }

        [Fact]
        public void BrainSlot_AnOldFileAndANewFile_LoadTheSameParams()
        {
            var registry = new BehaviorRegistry();
            string? Load(JsonNode paramsNode)
            {
                using var repo = new EntityRepository();
                SimHostComponentRegistry.RegisterAll(repo);
                var e = repo.CreateEntity();
                var slot = new JsonObject { ["Name"] = "ClearBuilding", ["Params"] = paramsNode, ["Origin"] = "Superior" };
                new BrainSnapshotTranslator(registry).Inject(repo, e, new Dictionary<string, object> { [BrainSnapshotTranslator.BehaviorKey] = slot }, new NullGuidResolver());
                return ((ISimulationView)repo).GetManagedComponentRO<InitialBrainIntent>(e).Behavior?.Params;
            }
            Assert.Equal(Params, Load(JsonValue.Create(Params)!));
            Assert.Equal(Normalised(Params), Normalised(Load(JsonNode.Parse(Params)!)!));
        }

        // ── the shipped corpus: no escaped params left (the one-time rewrite, and every generator since) ─────────────────────────────

        private static readonly Regex Escaped = new("\"(Params|params|behaviorParams|BehaviorParams)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\")", RegexOptions.Compiled);

        [Fact]
        public void TheShippedScenarios_CarryNoEscapedParams()
        {
            var offenders = new List<string>();
            foreach (var root in new[] { Path.Combine(TestRepo.Root, "scenarios"), Path.Combine(TestRepo.Root, "recipes", "scenarios") })
                foreach (var f in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
                    foreach (Match m in Escaped.Matches(File.ReadAllText(f)))
                        // only a string that IS a JSON object / array would have been written as itself
                        if (JsonTextConverter.IsStructured(JsonDocument.Parse(m.Groups[2].Value).RootElement.GetString()))
                        { offenders.Add(Path.GetRelativePath(TestRepo.Root, f)); break; }
            Assert.True(offenders.Count == 0, "escaped params (run python3 scripts/scenario_json_readable.py): " + string.Join(", ", offenders));
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static JsonNode? PropertyOf(JsonNode obj, string name)
        {
            foreach (var kv in obj.AsObject())
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            throw new Xunit.Sdk.XunitException($"no '{name}' in {obj.ToJsonString()}");
        }

        private sealed class NullGuidResolver : IGuidResolver
        {
            public string Resolve(Entity entity) => string.Empty;
            public Entity Resolve(string guid) => Entity.Null;
        }

        private static string Normalised(string json) => JsonNode.Parse(json)!.ToJsonString();
    }
}

using System.Text.Json.Nodes;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐ CE-3375 (📄 docs/UX/DESIGN_Scenario_Presets.md §1, D1, D4) — <c>header.presets</c>: written and read back in order with their variant choice and ONLY the
    /// changed args; a stored "Default" is ignored (it is implicit); a preset's load args carry <c>$variant</c> only when not as authored; only as-authored
    /// (or number 0) is saveable.
    /// 🔴 Red-proof (compiles): <c>ScenarioHeaderJson.Read</c> not reading Presets ⇒ the round trip red.
    /// </summary>
    public class ScenarioPresetsHeaderTests
    {
        [Fact]
        public void Presets_RoundTrip_InOrder_WithTheirVariantAndOnlyTheChangedArgs()
        {
            var h = new ScenarioHeader("Hrot.Scenario")
            {
                Presets = new[]
                {
                    new ScenarioPreset("Easy", PresetVariant.AsAuthored, new JsonObject { ["speed"] = 2.5 }),
                    new ScenarioPreset("Night attack", new PresetVariant(PresetVariantKind.Number, 3), new JsonObject { ["side"] = "Hostile" }),
                    new ScenarioPreset("Surprise", PresetVariant.RandomEachRun),
                },
            };
            var node = new JsonObject();
            ScenarioHeaderJson.Write(node, h);
            Assert.Contains("\"Variant\":\"AsAuthored\"", node.ToJsonString());
            Assert.Contains("\"Variant\":3", node.ToJsonString());
            var back = ScenarioHeaderJson.Read(new JsonObject { ["Header"] = node })!.Presets!;
            Assert.Equal(new[] { "Easy", "Night attack", "Surprise" }, new[] { back[0].Name, back[1].Name, back[2].Name });
            Assert.Equal(PresetVariant.AsAuthored, back[0].Variant);
            Assert.Equal(new PresetVariant(PresetVariantKind.Number, 3), back[1].Variant);
            Assert.Equal(PresetVariantKind.Random, back[2].Variant.Kind);
            Assert.Equal(2.5, back[0].Args!["speed"]!.GetValue<double>());
            Assert.Null(back[2].Args);
        }

        [Fact]
        public void AStoredDefault_IsIgnored_ItIsImplicit()
        {
            var dom = JsonNode.Parse("""{"Header":{"Presets":[{"Name":"Default","Variant":"AsAuthored"},{"Name":"B","Variant":"Random"}]}}""")!.AsObject();
            var presets = ScenarioHeaderJson.Read(dom)!.Presets!;
            Assert.Single(presets);
            Assert.Equal("B", presets[0].Name);
        }

        [Fact]
        public void APresetsLoadArgs_CarryTheVariant_OnlyWhenNotAsAuthored_AndOnlyAsAuthoredIsSaveable()
        {
            Assert.Equal("""{"speed":2}""", new ScenarioPreset("E", PresetVariant.AsAuthored, new JsonObject { ["speed"] = 2 }).ArgsJson(() => 9));
            Assert.Equal("""{"$variant":3}""", new ScenarioPreset("N", new PresetVariant(PresetVariantKind.Number, 3)).ArgsJson(() => 9));
            Assert.Equal("""{"$variant":9}""", new ScenarioPreset("R", PresetVariant.RandomEachRun).ArgsJson(() => 9));
            Assert.True(PresetVariant.AsAuthored.Saveable);
            Assert.True(new PresetVariant(PresetVariantKind.Number, 0).Saveable);
            Assert.False(new PresetVariant(PresetVariantKind.Number, 3).Saveable);
            Assert.False(PresetVariant.RandomEachRun.Saveable);
        }
    }
}

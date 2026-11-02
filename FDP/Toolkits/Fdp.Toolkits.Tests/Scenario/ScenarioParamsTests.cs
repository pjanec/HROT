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
    }
}

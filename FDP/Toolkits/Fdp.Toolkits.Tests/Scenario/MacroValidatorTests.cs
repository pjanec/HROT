using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Scenario
{
    /// <summary>
    /// ⭐⭐ Macros S3a ⑥ (<c>docs/DESIGN_Scenario_Macros.md</c> SR-149, SR-161, SR-162, SR-156, SR-188) — the validator the load's PREPARE runs:
    /// every problem of every instance, recursively through the macro files, named by its path, BEFORE anything is created; and a key
    /// two entities carry resolves to the first (a warning, not a refusal).
    /// </summary>
    public sealed class MacroValidatorTests
    {
        private static string Macro(string entities, string paramsJson = """[{"Name": "side", "TypeId": "ForceId", "Default": "Hostile"}]""",
                                    bool relocatable = true) => $$"""
            { "header": {"Kind": "macro", "Anchor": {"Position": [0, 0, 0], "Heading": 0}, "Params": {{paramsJson}}{{(relocatable ? "" : ", \"Relocatable\": false")}}},
              "entities": { {{entities}} } }
            """;

        private static string Scenario(string entities) => $$"""{ "header": {}, "entities": { {{entities}} } }""";

        private static string Instance(string key, string macro, string args = "{}", float x = 0) => $$$"""
            "{{{key}}}": { "EntityInfo": {"Name": "inst-{{{key[..4]}}}"}, "SimTransform": {"Position": [{{{x}}}, 0, 0]},
                           "MacroInstance": {"Macro": "{{{macro}}}", "Args": {{{args}}}} }
            """;

        private const string Man = """
            "aaaa0000-0000-4000-8000-000000000001": { "EntityInfo": {"Name": "Lead", "ForceId": "Hostile"},
                "ParamBindings": {"Bindings": {"EntityInfo.ForceId": "side"}},
                "Behavior": {"Name": "Move", "Params": "{\"Target\": \"^side\"}"} }
            """;

        private static InMemoryMacroLibrary Lib(params (string Path, string Json)[] files)
        {
            var lib = new InMemoryMacroLibrary();
            foreach (var (p, j) in files) lib.Files[p] = j;
            return lib;
        }

        [Fact]
        public void AGoodScenario_HasNoProblems()
        {
            var lib = Lib(("team", Macro(Man)));
            Assert.Empty(MacroValidator.Validate(Scenario(Instance("bbbb0000-0000-4000-8000-000000000001", "team", """{"side": "Friend"}""")), lib));
        }

        [Fact]
        public void EveryProblem_IsReported_NamingItsPath()
        {
            var badBindings = Macro("""
                "aaaa0000-0000-4000-8000-000000000002": { "EntityInfo": {"Name": "Lead"},
                    "ParamBindings": {"Bindings": {"EntityInfo.ForceId": "colour"}},
                    "Behavior": {"Name": "Move", "Params": "{\"Target\": \"^nowhere\", \"bind\": {\"Speed\": \"side\"}}"},
                    "When": {"Min": 3, "Bind": {"Min": "delay"}} }
                """);
            var lib = Lib(("team", Macro(Man)), ("bad", badBindings), ("cyc", Macro(Instance("cccc0000-0000-4000-8000-000000000003", "cyc"))),
                          ("pinned", Macro(Man, relocatable: false)), ("plain", """{"header": {}, "entities": {}}"""));
            var scenario = Scenario(string.Join(",", new[]
            {
                Instance("dddd0000-0000-4000-8000-000000000001", "missing"),
                Instance("dddd0000-0000-4000-8000-000000000002", "team", """{"colour": "red"}"""),
                Instance("dddd0000-0000-4000-8000-000000000003", "bad"),
                Instance("dddd0000-0000-4000-8000-000000000004", "cyc"),
                Instance("dddd0000-0000-4000-8000-000000000005", "pinned", x: 50),
                Instance("dddd0000-0000-4000-8000-000000000006", "plain"),
            }));

            var problems = MacroValidator.Validate(scenario, lib, "scenario 'x'");

            void Has(string fragment) => Assert.Contains(problems, p => p.Contains(fragment));
            Has("'missing' not found");
            Has("arg 'colour' is not a param of macro 'team'");
            Has("ParamBindings 'EntityInfo.ForceId' names 'colour'");
            Has("bound to '^nowhere'");
            Has("a 'bind' key inside params is refused");
            Has("bound to '^delay'");
            Has("macro cycle");
            Has("is pinned at its anchor");
            Has("'plain' is not a macro");
            Assert.All(problems, p => Assert.StartsWith("scenario 'x' / inst-", p));   // every one names its path
        }

        [Fact]
        public void APinnedMacro_AtItsAnchor_IsFine()
        {
            var lib = Lib(("pinned", Macro(Man, relocatable: false)));
            Assert.Empty(MacroValidator.Validate(Scenario(Instance("eeee0000-0000-4000-8000-000000000001", "pinned", x: 0)), lib));
        }

        [Fact]
        public void SR188_AKeyTwoEntitiesCarry_ResolvesToTheFirst_AndIsReportedAmbiguous()
        {
            var scope = new ScenarioRefScope("test");
            scope.Add(new EntityRef(10), "SL", Guid.Empty, null);
            scope.Add(new EntityRef(20), "SL", Guid.Empty, null);

            Assert.True(scope.TryResolve(RefSpec.ByKey("SL"), out var hit));
            Assert.Equal(10, hit.NetworkId);
            Assert.Equal(2, scope.Ambiguous["SL"]);
            Assert.False(scope.TryResolve(RefSpec.ByKey("Nobody"), out _));   // a MISSING key still resolves to nothing (RefResolve throws)
        }

        /// <summary>⭐ Macros S3b B2 (SR-101) — a ONE-OF-N instance: every choice is checked (any variant may pick it), each named.</summary>
        [Fact]
        public void B2_AOneOfNInstance_ChecksEveryChoice()
        {
            var lib = Lib(("team", Macro(Man)));
            string inst = """
                "bbbb0000-0000-4000-8000-000000000009": { "EntityInfo": {"Name": "choice"}, "SimTransform": {"Position": [0, 0, 0]},
                    "MacroInstance": {"Macro": ["team", {"macro": "ghost", "weight": 2}], "Args": {}} }
                """;
            var problems = MacroValidator.Validate(Scenario(inst), lib);
            var p = Assert.Single(problems);
            Assert.Contains("choice 'ghost'", p);
        }
    }
}

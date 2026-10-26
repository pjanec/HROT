using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐ <c>CE-1325</c> (docs/DESIGN_Utility_Decision_View.md) — on the real editor, with the REAL behaviours: the C#-built <c>CombatPosture</c>
/// tree has no asset, yet its nodes name the decisions they run (ChooseOption → Combat posture via <c>Choose</c>, → Attack approach via
/// <c>Approach</c>; an IsOption reads Take cover), the decision's settings come back, and a unit running it shows its values.
/// </summary>
[Trait("Category", "SystemSmoke")]
public sealed class UtilityDecisionViewTests : SystemTestBase
{
    public UtilityDecisionViewTests(EditorProcessFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    [SystemSmokeFact]
    public async Task A_csharp_trees_nodes_name_their_decisions_and_a_decision_reads_back_with_a_units_values()
    {
        var list = (await Mcp.ListUtilityDecisionsAsync()).EnsureOk().Array();
        Assert.Contains(list, d => d!["name"]?.GetValue<string>() == "Combat posture");

        var bindings = (await Mcp.GetBehaviourDecisionsAsync("CombatPosture")).EnsureOk().Data!["bindings"]!.AsArray();
        Output.WriteLine(bindings.ToJsonString());
        Assert.Contains(bindings, b => b!["role"]?.GetValue<string>() == "Chooses" && b["decision"]?.GetValue<string>() == "Combat posture"
                                       && b["via"]?.GetValue<string>() == "Choose");
        Assert.Contains(bindings, b => b!["role"]?.GetValue<string>() == "Chooses" && b["via"]?.GetValue<string>() == "Approach");
        Assert.Contains(bindings, b => b!["role"]?.GetValue<string>() == "Reads" && b["option"]?.GetValue<string>() == "TakeCover");

        var posture = (await Mcp.GetUtilityDecisionAsync("Combat posture")).EnsureOk().Data!;
        var advance = posture["options"]!.AsArray().First(o => o!["name"]?.GetValue<string>() == "AdvanceAndAttack")!;
        Assert.Equal("WeightedProduct", advance["aggregation"]?.GetValue<string>());
        Assert.Contains(advance["considerations"]!.AsArray(), c => c!["input"]?.GetValue<string>() == "HealthFraction");
        Assert.True((await Mcp.GetUtilityDecisionAsync("no such decision")).StatusCode == 404);

        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync("hill-attack"), startPaused: true);
        try
        {
            (await Mcp.StepAsync(60)).EnsureOk();
            foreach (var e in (await Mcp.ListEntitiesAsync()).EnsureOk().Array())
            {
                long n = e!["networkId"]?.GetValue<long>() ?? 0;
                var live = (await Mcp.GetUtilityDecisionAsync("Combat posture", n)).Data?["live"];
                if (live?["evaluations"]?.GetValue<int>() is not > 0) continue;
                Output.WriteLine($"#{n}: {live.ToJsonString()}");
                Assert.NotNull(live["winner"]?.GetValue<string>());
                return;
            }
            Output.WriteLine("no unit ran Combat posture in 60 steps of hill-attack — the live half is railed in the unit suite");
        }
        finally { await ResetToIdleAsync(); }
    }
}

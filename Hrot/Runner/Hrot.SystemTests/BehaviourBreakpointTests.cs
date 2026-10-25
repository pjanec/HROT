using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐ <c>CE-1323</c> (Behaviour Debugging §9) — a BEHAVIOUR BREAKPOINT set and seen through the AI debug API alone, on the real editor:
/// a state name that is not in the unit's machine is a 400 that lists the states it has; a breakpoint on leaving any of them, scoped to
/// the unit, pauses the run when its machine moves, and <c>get_breakpoint_status</c> names what fired; a continue does not stop again
/// on the same step. 🔒 User: <i>"make sure there are ways how to set the breakpoints … from the ai debug api (and that ai debug api can
/// find out that the breakpoint fired)"</i>.
/// </summary>
[Trait("Category", "SystemSmoke")]
public sealed class BehaviourBreakpointTests : SystemTestBase
{
    public BehaviourBreakpointTests(EditorProcessFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    [SystemSmokeFact]
    public async Task A_state_machine_step_is_a_breakpoint_the_api_sets_and_sees_fire()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync("hill-attack"), startPaused: true);
        try
        {
            (await Mcp.StepAsync(5)).EnsureOk();

            // find a unit running a state machine: the probe's 400 names its states
            long unit = 0; string behaviour = ""; string[] states = Array.Empty<string>();
            foreach (var e in (await Mcp.ListEntitiesAsync()).EnsureOk().Array())
            {
                long n = e!["networkId"]?.GetValue<long>() ?? 0;
                var slots = (await Mcp.GetEntitySlotsAsync(n)).Field("slots") as JsonArray;
                if (slots?.FirstOrDefault(s => s?["owner"]?.GetValue<string>() == "Task")?["behaviour"]?.GetValue<string>() is not { } name) continue;
                var probe = await Mcp.AddBreakpointAsync(Step(name, "StateEnter", "State", "__no_such_state__"), n);
                Assert.False(probe.Ok, "a state that is not in the machine is refused");
                if (probe.Error is not { } err || !err.Contains("its states:")) continue;
                states = err[(err.IndexOf("its states:", StringComparison.Ordinal) + "its states:".Length)..].TrimEnd('.', ' ')
                    .Split(", ").Where(s => s.Length > 0 && !s.StartsWith("__") && s != "…").ToArray();
                if (states.Length == 0) continue;
                unit = n; behaviour = name;
                break;
            }
            Output.WriteLine($"unit {unit} runs {behaviour}: {string.Join(", ", states)}");
            Assert.True(unit != 0, "hill-attack has a unit whose task is a state machine");

            foreach (var s in states)
                (await Mcp.AddBreakpointAsync(Step(behaviour, "StateExit", "State", s), unit)).EnsureOk();
            var list = (await Mcp.ListBreakpointsAsync()).EnsureOk().Array();
            Assert.Contains(list, b => b!["kind"]?.GetValue<string>() == "Node" && b["filterNetworkId"]?.GetValue<long>() == unit);

            (await Mcp.PlayAsync()).EnsureOk();
            JsonNode? hit = null;
            for (int i = 0; i < 120 && hit is null; i++)
            {
                await Task.Delay(500);
                var st = (await Mcp.GetBreakpointHitsAsync()).EnsureOk();
                if (st.Data?["isPaused"]?.GetValue<bool>() == true) hit = st.Data["lastHit"];
            }
            Output.WriteLine(hit?.ToJsonString() ?? "(no hit in 60 s)");
            Assert.NotNull(hit);
            Assert.Equal(unit, hit!["networkId"]?.GetValue<long>());
            Assert.StartsWith("left ", hit["what"]?.GetValue<string>());

            // a continue does not stop again on the same step
            long pausedTick = (await Mcp.GetBreakpointHitsAsync()).EnsureOk().Data!["pausedTick"]!.GetValue<long>();
            (await Mcp.ContinueFromBreakpointAsync()).EnsureOk();
            await Task.Delay(300);
            var again = (await Mcp.GetBreakpointHitsAsync()).EnsureOk().Data!;
            Assert.False(again["isPaused"]?.GetValue<bool>() == true && again["pausedTick"]?.GetValue<long>() == pausedTick,
                "stopped again on the same tick's step");
        }
        finally
        {
            foreach (var b in ((await Mcp.ListBreakpointsAsync()).Data as JsonArray) ?? new JsonArray())
                if (b?["id"]?.GetValue<string>() is { } id) await Mcp.RemoveBreakpointAsync(id);
            await ResetToIdleAsync();
        }
    }

    private static JsonObject Step(string behaviour, string kind, string field, string value)
        => new() { ["$type"] = "BehaviourStep", ["Behaviour"] = behaviour, ["Kind"] = kind, [field] = value };
}

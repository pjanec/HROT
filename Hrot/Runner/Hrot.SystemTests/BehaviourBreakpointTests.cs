using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// ⭐⭐ <c>CE-1323</c> (Behaviour Debugging §9) — a BEHAVIOUR BREAKPOINT set and seen through the AI debug API alone, on the real editor (hsm-channel-e2e):
/// a state name that is not in the unit's machine is a 400 that lists the states it has; a breakpoint on leaving any of them, scoped to
/// the unit, pauses the run when its machine moves, and <c>get_breakpoint_status</c> names what fired; a continue does not stop again
/// on the same step. 🔒 User: <i>"make sure there are ways how to set the breakpoints … from the ai debug api (and that ai debug api can
/// find out that the breakpoint fired)"</i>.
/// </summary>
[Trait("Category", "SystemSmoke")]
public sealed class BehaviourBreakpointTests : SystemTestBase
{
    public BehaviourBreakpointTests(EditorProcessFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    /// <remarks>On <c>hsm-channel-e2e</c>: the "Open=false" Bradley runs <c>HsmChannelE2E</c> and stays in Driving until its polled guard
    /// (<c>Params.Guard.EngageTarget</c>) is true — so the test sets the breakpoints FIRST, then flips the guard live (CE-1320), and the
    /// Driving → Firing step happens after them.</remarks>
    [SystemSmokeFact]
    public async Task A_state_machine_step_is_a_breakpoint_the_api_sets_and_sees_fire()
    {
        const string Machine = "HsmChannelE2E";
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync("hsm-channel-e2e"), startPaused: true);
        try
        {
            (await Mcp.StepAsync(10)).EnsureOk();

            long unit = 0;
            foreach (var e in (await Mcp.ListEntitiesAsync()).EnsureOk().Array())
            {
                long n = e!["networkId"]?.GetValue<long>() ?? 0;
                var slots = (await Mcp.GetEntitySlotsAsync(n)).Field("slots") as JsonArray;
                foreach (var sl in slots ?? new JsonArray())
                    Output.WriteLine($"  #{n} {sl?["owner"]} {sl?["role"]} {sl?["behaviour"]} = {sl?["value"]?.ToJsonString()}");
                var p = slots?.FirstOrDefault(s => s?["owner"]?.GetValue<string>() == "Task" && s?["role"]?.GetValue<string>() == "Params"
                                                  && s?["behaviour"]?.GetValue<string>() == Machine);
                if (p?["value"]?["In"]?["Guard"]?["EngageTarget"]?.GetValue<bool>() == false) { unit = n; break; }
            }
            Assert.True(unit != 0, "hsm-channel-e2e has its Open=false Bradley running HsmChannelE2E");

            // a name that is not in the machine is refused, and the answer lists what is
            var probe = await Mcp.AddBreakpointAsync(Step(Machine, "StateEnter", "State", "Retreating"), unit);
            Output.WriteLine($"probe: {probe.StatusCode} {probe.Error}");
            Assert.False(probe.Ok);
            Assert.Contains("Driving", probe.Error);
            Assert.Contains("Firing", probe.Error);

            var set = (await Mcp.AddBreakpointAsync(Step(Machine, "StateEnter", "State", "Firing"), unit)).EnsureOk();
            Output.WriteLine($"set: {set.Data?.ToJsonString()}");
            var listed = (await Mcp.ListBreakpointsAsync()).EnsureOk().Array();
            Assert.Contains(listed, b => b!["kind"]?.GetValue<string>() == "Node" && b["filterNetworkId"]?.GetValue<long>() == unit);
            (await Mcp.StepAsync(3)).EnsureOk();                                    // the scoped breakpoint arms his traces
            Assert.False((await Mcp.GetBreakpointHitsAsync()).EnsureOk().Data?["isPaused"]?.GetValue<bool>() == true, "still Driving");

            (await Mcp.StageSlotFieldAsync(unit, new JsonObject
            {
                ["owner"] = "task", ["behaviour"] = Machine, ["role"] = "params", ["path"] = "In.Guard.EngageTarget", ["value"] = true,
            })).EnsureOk();
            (await Mcp.PlayAsync()).EnsureOk();
            JsonNode? status = null;
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                var st = (await Mcp.GetBreakpointHitsAsync()).EnsureOk();
                if (st.Data?["isPaused"]?.GetValue<bool>() == true) { status = st.Data; break; }
            }
            Output.WriteLine(status?.ToJsonString() ?? "(no hit in 30 s)");
            Assert.NotNull(status);
            var hit = status!["lastHit"]!;
            Assert.Equal(unit, hit["networkId"]?.GetValue<long>());
            Assert.Equal("entered Firing", hit["what"]?.GetValue<string>());

            // a continue does not stop again on the same step
            long pausedTick = status["pausedTick"]!.GetValue<long>();
            (await Mcp.ContinueFromBreakpointAsync()).EnsureOk();
            await Task.Delay(1000);
            var again = (await Mcp.GetBreakpointHitsAsync()).EnsureOk().Data!;
            Output.WriteLine($"after continue: {again.ToJsonString()}");
            Assert.False(again["isPaused"]?.GetValue<bool>() == true, "stopped again after the continue");
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

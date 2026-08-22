using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// One case per capability the system offers over the AI-debug API (task H4) — the standing smoke
/// that says "the whole stack still does this", driven exactly as a human or an agent would drive
/// it.
///
/// <para><b>Every case is self-sufficient.</b> xUnit does not promise an order, and the collection
/// shares ONE editor, so a case that assumed the previous one left the world loaded would fail
/// depending on the order it happened to run in. Each case establishes what it needs and puts back
/// what it changed.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
public sealed class CapabilitySmokeTests : SystemTestBase
{
    public CapabilitySmokeTests(EditorProcessFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    /// <summary>The scenario the cases drive. Curated worlds are seeded from git on editor start.</summary>
    private const string PreferredScenario = "hill-attack";

    // ── ① status ───────────────────────────────────────────────────────────────

    [SystemSmokeFact]
    public async Task Status_reports_a_live_editor()
    {
        var status = (await Mcp.GetStatusAsync()).EnsureOk();

        // clusterState is the field that distinguishes "the process answered" from "the editor
        // exists": it is read from the live EditorApplication, not a constant.
        Assert.False(string.IsNullOrWhiteSpace(status.String("clusterState")));
        Assert.True(status.Double("simTime") >= 0);
        Output.WriteLine($"status: {status.Data?.ToJsonString()}");
    }

    // ── ② curated scenario load ────────────────────────────────────────────────

    [SystemSmokeFact]
    public async Task Curated_scenarios_are_seeded_and_loadable()
    {
        var scenarios = (await Mcp.ListScenariosAsync()).EnsureOk().Array();
        Assert.NotEmpty(scenarios);

        var name = await AnyCuratedScenarioAsync(PreferredScenario);
        var load = (await Mcp.LoadScenarioAsync(name)).EnsureOk();

        // waitForReady means the HOST polled the cluster to OperatingEdit before answering, so a
        // successful return is the load completing — not merely being accepted.
        Assert.True(load.Bool("awaited"), $"expected an awaited load, got {load.Data?.ToJsonString()}");

        var status = (await Mcp.GetStatusAsync()).EnsureOk();
        Assert.Equal(name, status.String("scenario"));
    }

    // ── ③ entity list + dump ───────────────────────────────────────────────────

    [SystemSmokeFact]
    public async Task Entities_can_be_listed_and_inspected()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario));

        var entities = await WaitUntilAsync(
            () => Mcp.ListEntitiesAsync(),
            r => r.Ok && r.Array().Count > 0,
            "the loaded scenario to produce entities");

        var first = entities.Array()[0]!;
        long networkId = first["networkId"]!.GetValue<long>();

        var dump = (await Mcp.GetEntityAsync(networkId)).EnsureOk();
        // The dump is serialized from a DTO and keeps PascalCase, unlike the hand-built list rows —
        // reading it case-insensitively is what lets one client serve both shapes.
        Assert.Equal(networkId, dump.Long("NetworkId"));
        Assert.NotNull(dump.Field("Components"));

        Output.WriteLine($"{entities.Array().Count} entities; inspected {networkId} " +
                         $"({first["name"]?.GetValue<string>()})");
    }

    [SystemSmokeFact]
    public async Task An_unknown_entity_is_a_404_that_says_where_to_look()
    {
        var missing = await Mcp.GetEntityAsync(999_999_999L);

        Assert.False(missing.Ok);
        Assert.Equal(404, missing.StatusCode);
        // The existing prose-hint habit that MX8 promotes into a structured field.
        Assert.Contains("GET /entities", missing.Error ?? "");
    }

    // ── ④ preview + play advance time ──────────────────────────────────────────

    [SystemSmokeFact]
    public async Task Preview_and_play_advance_simulation_time()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            var before = (await Mcp.GetStatusAsync()).EnsureOk().Double("simTime");

            (await Mcp.PlayAsync()).EnsureOk();

            var after = await WaitForSimTimeAsync(before);
            Assert.True(after.Double("simTime") > before,
                $"simTime did not advance past {before}");

            (await Mcp.PauseAsync()).EnsureOk();
            var paused = (await Mcp.GetSimStateAsync()).EnsureOk();
            Assert.True(paused.Bool("isPaused"));
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    [SystemSmokeFact]
    public async Task Stepping_advances_a_paused_simulation()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            (await Mcp.PauseAsync()).EnsureOk();
            var before = (await Mcp.GetStatusAsync()).EnsureOk().Double("simTime");

            (await Mcp.StepAsync(5)).EnsureOk();

            var after = await WaitUntilAsync(
                () => Mcp.GetStatusAsync(),
                r => r.Ok && r.Double("simTime") > before,
                "a discrete step to move the clock");
            Output.WriteLine($"simTime {before} → {after.Double("simTime")} over 5 steps");
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    // ── ⑤ breakpoint: set → play → hit ─────────────────────────────────────────

    [SystemSmokeFact]
    public async Task A_breakpoint_can_be_set_listed_and_removed()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);

        // PropertyMatch + Changed trips on any movement of the watched field — the condition least
        // dependent on a particular scenario's geometry.
        var condition = new JsonObject
        {
            ["$type"] = "PropertyMatch",
            ["ComponentType"] = "SimTransform",
            ["PropertyPath"] = "Position.X",
            ["Operator"] = "Changed",
            ["Predicate"] = new JsonObject
            {
                ["$type"] = "Numeric",
                ["MinValue"] = double.MinValue,
                ["MaxValue"] = double.MaxValue,
            },
        };

        var added = (await Mcp.AddBreakpointAsync(condition, name: "smoke-position-changed")).EnsureOk();
        var id = added.String("breakpointId");
        Assert.False(string.IsNullOrWhiteSpace(id));

        try
        {
            var list = (await Mcp.ListBreakpointsAsync()).EnsureOk().Array();
            Assert.Contains(list, n => n?["id"]?.GetValue<string>() == id
                                    || n?["Id"]?.GetValue<string>() == id);

            // The hit surface answers whether or not anything has tripped yet.
            var hits = (await Mcp.GetBreakpointHitsAsync()).EnsureOk();
            Assert.NotNull(hits.Field("isPaused"));
        }
        finally
        {
            (await Mcp.RemoveBreakpointAsync(id!)).EnsureOk();
            await ResetToIdleAsync();
        }
    }

    [SystemSmokeFact]
    public async Task A_malformed_breakpoint_condition_is_rejected()
    {
        var bad = new JsonObject { ["$type"] = "NoSuchPredicateArm", ["whatever"] = 1 };

        var result = await Mcp.AddBreakpointAsync(bad);

        Assert.False(result.Ok);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("condition", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        // ⭐ This is the case MX8 extends: today the error is prose only. Once the structured hint
        // lands, the same rejection must also carry hint.seeEndpoint == "GET /breakpoint-types".
        Output.WriteLine($"rejection: {result.Error}");
    }

    // ── ⑥ live mutation: write a component, read it back ───────────────────────

    [SystemSmokeFact]
    public async Task A_component_can_be_written_and_read_back()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            var entity = await FirstEntityWithComponentAsync("SimTransform");

            // ⚠ Asymmetric shapes, measured: the DUMP serializes Vector3 as [x,y,z], but the PATCH
            // parser goes through StructEdit and wants the field object {X,Y,Z}. Writing the array
            // form back is rejected with "expected Vector3" — an easy and costly assumption.
            var patch = new JsonObject
            {
                ["Position"] = new JsonObject { ["X"] = 1234.5, ["Y"] = 0.0, ["Z"] = -678.25 },
            };
            var write = await Mcp.SetComponentAsync(entity, "SimTransform", patch);
            write.EnsureOk();

            var readBack = await WaitUntilAsync(
                () => Mcp.GetEntityAsync(entity),
                r => r.Ok && PositionX(r) is > 1234.0 and < 1235.0,
                $"entity {entity} to carry the written SimTransform.Position.X");

            Output.WriteLine($"wrote and read back Position.X={PositionX(readBack)}");
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    // ── ⑦ checkpoint → mutate → diff ───────────────────────────────────────────

    [SystemSmokeFact]
    public async Task A_baseline_can_be_captured_and_compared()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            var capture = (await Mcp.DiffCaptureAsync()).EnsureOk();
            var baselineId = capture.String("baselineId") ?? capture.String("id");
            Assert.False(string.IsNullOrWhiteSpace(baselineId),
                $"no baseline id in {capture.Data?.ToJsonString()}");

            var entity = await FirstEntityWithComponentAsync("SimTransform");
            (await Mcp.SetComponentAsync(entity, "SimTransform",
                new JsonObject { ["Position"] = new JsonObject { ["X"] = 4321.0, ["Y"] = 0.0, ["Z"] = 0.0 } })).EnsureOk();

            var diff = (await Mcp.DiffCompareAsync(baselineId!)).EnsureOk();
            Output.WriteLine($"diff: {Truncate(diff.Data?.ToJsonString())}");
            Assert.NotNull(diff.Data);
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    [SystemSmokeFact]
    public async Task An_unknown_baseline_is_rejected_with_a_pointer()
    {
        var result = await Mcp.DiffCompareAsync("no-such-baseline");

        Assert.False(result.Ok);
        Assert.Contains("/diff/capture", result.Error ?? "");
    }

    // ── ⑧ replay ───────────────────────────────────────────────────────────────
    //
    // ⛔ The record→replay ROUND TRIP is pinned in KnownDefectRails, not here: /recording/stop
    // exits preview, which currently aborts the editor (HN-001). What is still assertable — and
    // worth asserting, because it is the surface an agent hits first — is that the replay group
    // answers coherently and refuses a bad load with a usable error.

    [SystemSmokeFact]
    public async Task The_replay_surface_reports_no_replay_until_one_is_loaded()
    {
        var status = (await Mcp.GetReplayStatusAsync()).EnsureOk();

        Assert.False(status.Bool("replayActive"));
        Assert.Equal(0, status.Int("totalFrames"));
    }

    [SystemSmokeFact]
    public async Task Loading_a_missing_recording_is_rejected_not_crashed()
    {
        var missing = Path.Combine(Fixture.StagingRoot, "no-such-recording.fdp");

        var result = await Mcp.LoadReplayAsync(missing);

        Assert.False(result.Ok);
        Assert.Contains("not found", result.Error ?? "", StringComparison.OrdinalIgnoreCase);

        // The editor must still be serving — a bad path is a rejected request, not a dead process.
        (await Mcp.GetStatusAsync()).EnsureOk();
    }

    // ── ⑨ fault injection (Group L) ────────────────────────────────────────────

    [SystemSmokeFact]
    public async Task The_attribute_schema_is_available_for_fault_injection()
    {
        var schema = (await Mcp.GetAttributesSchemaAsync()).EnsureOk();
        Assert.NotNull(schema.Data);
        Output.WriteLine($"attributes schema: {Truncate(schema.Data?.ToJsonString())}");
    }

    // ── ⑩ trace observe ────────────────────────────────────────────────────────

    [SystemSmokeFact]
    public async Task A_behaviour_trace_can_be_armed_and_read()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            var entity = await FirstEntityAsync();

            var armed = await Mcp.ObserveTraceAsync(entity, on: true);
            // Arming can legitimately refuse for an entity with no behaviour tree; what must hold
            // is that the tracer ANSWERS rather than faulting the request.
            Output.WriteLine($"observe: ok={armed.Ok} {armed.Data?.ToJsonString() ?? armed.Error}");
            Assert.InRange(armed.StatusCode, 200, 499);

            if (armed.Ok)
            {
                var trace = await Mcp.GetEntityTraceAsync(entity);
                Assert.True(trace.Ok, trace.Error);
                await Mcp.ObserveTraceAsync(entity, on: false);
            }
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    // ── discovery surfaces used by the agent-facing side ───────────────────────

    [SystemSmokeFact]
    public async Task Command_and_component_catalogs_are_discoverable()
    {
        var commands = (await Mcp.ListCommandsAsync()).EnsureOk();
        var components = (await Mcp.ListComponentsAsync()).EnsureOk();

        Assert.NotNull(commands.Data);
        Assert.NotNull(components.Data);
        Output.WriteLine($"{commands.Array().Count} commands, {components.Array().Count} components");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private Task<long> FirstEntityAsync() => FirstAddressableEntityAsync(null);

    private Task<long> FirstEntityWithComponentAsync(string component) => FirstAddressableEntityAsync(component);

    /// <summary>
    /// Returns an entity that is both LISTED and ADDRESSABLE.
    ///
    /// <para><c>GET /entities</c> enumerates the world while <c>GET /entities/{id}</c> resolves
    /// through the network-entity map, so an id can in principle be listed and not yet resolvable.
    /// Taking the first listed id on faith is what made three cases fail for a reason that had
    /// nothing to do with what they were testing. ⚠ This tolerates a straggler; it does NOT hide
    /// incoherence — if NOTHING listed resolves, it says so and fails.</para>
    /// </summary>
    private async Task<long> FirstAddressableEntityAsync(string? component)
    {
        var listed = await WaitUntilAsync(
            () => Mcp.ListEntitiesAsync(component: component),
            r => r.Ok && r.Array().Count > 0,
            component is null ? "at least one entity in the loaded world"
                              : $"at least one entity carrying {component}");

        var ids = listed.Array()
            .Select(n => n?["networkId"]?.GetValue<long>())
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        foreach (var id in ids)
        {
            if ((await Mcp.GetEntityAsync(id)).Ok)
                return id;
        }

        throw new McpRequestException(
            $"GET /entities listed {ids.Count} entities" +
            (component is null ? "" : $" carrying {component}") +
            $" but none of them resolved through GET /entities/{{id}}: [{string.Join(", ", ids)}]");
    }

    /// <summary>Reads <c>SimTransform.Position.X</c> out of an entity dump, whatever shape it took.</summary>
    private static double? PositionX(ApiResult dump)
    {
        var components = dump.Field("Components");
        var transform = components?["SimTransform"];
        var position = transform?["Position"];
        return position switch
        {
            JsonArray arr when arr.Count > 0 => arr[0]?.GetValue<double>(),
            JsonObject obj => obj["X"]?.GetValue<double>(),
            _ => null,
        };
    }

    private static string Truncate(string? s) => s is null ? "null" : s.Length <= 300 ? s : s[..300] + "…";
}

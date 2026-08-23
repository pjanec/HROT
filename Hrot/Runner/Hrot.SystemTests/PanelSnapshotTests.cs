using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// <b>Group T — the UI read without pixels (MX9), and Groups Q/R beside it.</b>
///
/// <para>These are the cases the whole observability programme exists for: assert what a panel SHOWS by
/// reading its view-model, not by comparing images. A panel's model is what its draw renders from, so a
/// field asserted here is a field the designer sees.</para>
///
/// <para>⚠ <b>Which panels are live depends on the perspective</b>, so these cases read whatever the
/// editor published rather than naming a panel that happens to be open today — a hard-coded panel id
/// would turn a layout change into a red that says nothing about the endpoint.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemPanels")]
public sealed class PanelSnapshotTests : SystemTestBase
{
    public PanelSnapshotTests(EditorProcessFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    /// <summary>
    /// The list distinguishes "instrumented" from "published this frame" — the distinction the two-set
    /// design exists for, and the one that keeps an empty assertion from reading as an empty UI.
    /// </summary>
    [SystemSmokeFact]
    public async Task The_panel_list_separates_instrumented_from_captured()
    {
        var panels = (await Mcp.GetPanelsAsync()).EnsureOk();

        Assert.True(panels.Bool("captureEnabled"),
            "capture is off, so no panel can publish — the debug API is supposed to turn it on");

        var registered = panels.Field("registered") as JsonArray;
        var captured   = panels.Field("captured") as JsonArray;
        Assert.NotNull(registered);
        Assert.NotNull(captured);
        Assert.NotEmpty(registered!);

        // Captured is a SUBSET of registered — a panel that published without declaring would mean the
        // two sets disagree about what exists.
        var registeredIds = registered!.Select(n => n!.GetValue<string>()).ToHashSet();
        foreach (var id in captured!.Select(n => n!.GetValue<string>()))
            Assert.Contains(id, registeredIds);

        Output.WriteLine($"{registered.Count} instrumented, {captured.Count} published this frame; "
                         + $"kinds: {panels.Field("kinds")?.AsObject().Count}");
    }

    /// <summary>
    /// <b>The point of the programme:</b> read a panel's model over HTTP and assert a FIELD of it.
    /// </summary>
    [SystemSmokeFact]
    public async Task A_panels_model_can_be_read_and_a_field_asserted()
    {
        var panels = (await Mcp.GetPanelsAsync()).EnsureOk();
        var captured = (panels.Field("captured") as JsonArray)!;
        Assert.NotEmpty(captured);

        var panelId = captured[0]!.GetValue<string>();
        var panel = (await Mcp.GetPanelAsync(panelId)).EnsureOk();

        Assert.Equal(panelId, panel.String("panelId"));
        // PanelKind is what cross-host conformance groups by, so it must never come back empty.
        Assert.False(string.IsNullOrWhiteSpace(panel.String("panelKind")),
            $"panel '{panelId}' published no PanelKind: {panel.Data?.ToJsonString()}");

        var model = panel.Field("model");
        Assert.NotNull(model);
        // The model is structured, not a formatted blob — an assertion must be able to reach a field.
        Assert.IsType<JsonObject>(model);

        Output.WriteLine($"{panelId} ({panel.String("panelKind")}): {model!.ToJsonString()[..Math.Min(200, model.ToJsonString().Length)]}");
    }

    /// <summary>
    /// A panel id nobody instrumented must say so, and differently from one that is instrumented but
    /// has not drawn — only the second is fixed by opening a window.
    /// </summary>
    [SystemSmokeFact]
    public async Task An_unknown_panel_is_refused_with_a_usable_error()
    {
        var result = await Mcp.GetPanelAsync("zzz-no-such-panel");

        Assert.False(result.Ok);
        Assert.Equal(404, result.StatusCode);
        Assert.Contains("zzz-no-such-panel", result.Error ?? "");
        Assert.Equal("GET /panels", result.HintEndpoint);

        Output.WriteLine($"rejection: {result.Error}");
    }

    /// <summary>
    /// The gizmo feed is the same snapshot one layer down: what the map draws, as data. It reports its
    /// own truncation, so a reader can tell a full frame from a clipped one.
    /// </summary>
    [SystemSmokeFact]
    public async Task The_gizmo_frame_reports_primitives_and_its_own_truncation()
    {
        var frame = (await Mcp.GetGizmoFrameAsync(max: 25)).EnsureOk();

        Assert.NotNull(frame.Field("count"));
        int count = frame.Int("count");

        var primitives = frame.Field("primitives") as JsonArray;
        Assert.NotNull(primitives);
        Assert.True(primitives!.Count <= 25, "the max was not honoured");

        if (count > 25)
            Assert.True(frame.Bool("truncated"),
                "the frame was clipped but did not say so — a reader would take the cap for the end");

        foreach (var p in primitives)
            Assert.False(string.IsNullOrWhiteSpace(p?["shape"]?.GetValue<string>()),
                $"a primitive came back with no shape: {p?.ToJsonString()}");

        Output.WriteLine($"{count} primitives this frame, {primitives.Count} emitted, "
                         + $"truncated={frame.Bool("truncated")}");
    }

    /// <summary>
    /// <b>Group R.</b> The convenience the design asked for: <c>state.position.x</c> without digging
    /// through the component dump — and it must agree with the dump it is a convenience over.
    /// </summary>
    [SystemSmokeFact]
    public async Task Entity_state_reads_the_well_known_fields()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync("hill-attack"), startPaused: true);
        try
        {
            var entity = await FirstListedEntityAsync();
            var state = (await Mcp.GetEntityStateAsync(entity)).EnsureOk();

            Assert.True(state.Bool("alive"));
            var position = state.Field("position");
            Assert.NotNull(position);
            Assert.NotNull(position!["x"]);

            // It is a projection of the same components, so it must not disagree with the full dump.
            var dump = (await Mcp.GetEntityAsync(entity)).EnsureOk();
            var dumped = dump.Field("Components")?["SimTransform"]?["Position"];
            if (dumped is JsonArray arr && arr.Count == 3)
            {
                double fromDump  = arr[0]!.GetValue<double>();
                double fromState = position["x"]!.GetValue<double>();
                Assert.True(Math.Abs(fromDump - fromState) < 0.001,
                    $"/state says x={fromState} but the component dump says {fromDump}");
            }

            Output.WriteLine($"entity {entity}: position.x={position["x"]}, speed={state.Field("speed")}");
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    /// <summary>
    /// <b>Group Q.</b> The blueprint catalogue an attach is addressed against, and the refusal for a
    /// name that is not in it.
    /// </summary>
    [SystemSmokeFact]
    public async Task Blueprints_are_listed_and_an_unknown_one_is_refused()
    {
        var blueprints = (await Mcp.GetBlueprintsAsync()).EnsureOk();
        var list = blueprints.Field("blueprints") as JsonArray;
        Assert.NotNull(list);
        Assert.NotEmpty(list!);

        foreach (var bp in list!)
        {
            Assert.False(string.IsNullOrWhiteSpace(bp?["name"]?.GetValue<string>()));
            // attachable is what an agent filters on before trying — it must always be answered.
            Assert.NotNull(bp?["attachable"]);
        }

        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync("hill-attack"), startPaused: true);
        try
        {
            var entity = await FirstListedEntityAsync();
            var refused = await Mcp.AttachBlueprintAsync(entity, "zzz-no-such-blueprint");

            Assert.False(refused.Ok);
            Assert.Equal(400, refused.StatusCode);
            Assert.Equal("GET /blueprints", refused.HintEndpoint);

            int attachable = list.Count(b => b?["attachable"]?.GetValue<bool>() ?? false);
            Output.WriteLine($"{list.Count} blueprints, {attachable} attachable; rejection: {refused.Error}");
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    private async Task<long> FirstListedEntityAsync()
    {
        var listed = await WaitUntilAsync(
            () => Mcp.ListEntitiesAsync(),
            r => r.Ok && r.Array().Count > 0,
            "at least one entity in the loaded world");

        return listed.Array()[0]!["networkId"]!.GetValue<long>();
    }
}

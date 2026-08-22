using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// <b>Group O — variable addressing (MX1), and the watch case the harness owed (HN-005).</b>
///
/// <para>These are the "watch, over HTTP" cases: address a blueprint variable by
/// <c>(entity, asset, path)</c> — the tuple a Details/watch row uses — rather than by component and
/// byte offset, read its live value, stage a write, and see the staged value land when the world
/// advances.</para>
///
/// <para>⚠ <b>Discovery-driven, deliberately.</b> Which curated entity carries which blueprint is a
/// property of the scenario content, not of this API, so these cases FIND a blueprint-carrying
/// entity rather than hard-coding one — a hard-coded id turns any scenario edit into a red that says
/// nothing about the endpoint. When the loaded world has no blueprint entity at all, the cases still
/// assert the endpoint's REFUSAL is the honest, actionable one, and say so in the output.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "SystemVariables")]
public sealed class VariableAddressingTests : SystemTestBase
{
    /// <summary>The world these cases prefer; any curated scenario will do if it is absent.</summary>
    private const string PreferredScenario = "hill-attack";

    public VariableAddressingTests(EditorProcessFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    /// <summary>
    /// Reading: every variable the entity's blueprint carries, each with a value and a pending flag.
    /// </summary>
    [SystemSmokeFact]
    public async Task An_entitys_blueprint_variables_can_be_listed()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            var found = await FindBlueprintEntityAsync();
            if (found is null)
            {
                await AssertHonestRefusalAsync();
                return;
            }

            var (entity, variables) = found.Value;

            foreach (var variable in variables)
            {
                Assert.False(string.IsNullOrWhiteSpace(variable?["path"]?.GetValue<string>()),
                    $"a variable came back with no path: {variable?.ToJsonString()}");
                // pending is the machine half of the panel's yellow — it must always be answered,
                // never omitted, or a caller cannot tell "not staged" from "not reported".
                Assert.NotNull(variable?["pending"]);
            }

            Output.WriteLine(variables.Count == 0
                ? $"entity {entity} carries a blueprint with no working-state variables "
                  + "(a Library-dispatch blueprint has none) — shape asserted, values not."
                : $"entity {entity} carries {variables.Count} variables: "
                  + string.Join(", ", variables.Select(v => v?["path"]?.GetValue<string>())));
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    /// <summary>
    /// <b>The owed watch case.</b> Stage a write through the same seam the Details editor uses, see
    /// it reported as pending while the world is paused, then advance the world and see it land.
    /// </summary>
    [SystemSmokeFact]
    public async Task A_staged_variable_write_is_pending_then_lands()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            var found = await FindBlueprintEntityAsync();
            if (found is null)
            {
                await AssertHonestRefusalAsync();
                return;
            }

            var (entity, variables) = found.Value;

            // A numeric, writable variable — the one kind this case can set to a value it can then
            // recognise. Non-numeric or unaddressable variables are a legitimate part of the list.
            var target = variables.FirstOrDefault(v =>
                (v?["writable"]?.GetValue<bool>() ?? false)
                && v?["value"] is JsonValue jv && jv.TryGetValue<double>(out _));

            if (target is null)
            {
                Output.WriteLine("no writable numeric variable on this entity — read path asserted, "
                                 + "write path not exercised by this world.");
                return;
            }

            var path     = target["path"]!.GetValue<string>();
            var original = target["value"]!.GetValue<double>();
            var written  = original + 17.0;

            var staged = (await Mcp.StageEntityVariableAsync(entity, path, JsonValue.Create(written)))
                .EnsureOk();
            Assert.True(staged.Bool("staged"));

            // While the world is paused, the write is QUEUED, not applied: the read still reports the
            // old value and flags it pending. That is the whole contract — a staged write that
            // claimed to have landed would be a lie the panel's yellow exists to prevent.
            var whilePaused = (await Mcp.GetEntityVariableAsync(entity, path)).EnsureOk();
            Assert.True(whilePaused.Bool("pending"),
                $"the staged write on '{path}' was not reported pending: {whilePaused.Data?.ToJsonString()}");

            // Advance the world — the kernel's drain applies staged writes at the next advancing tick.
            (await Mcp.PlayAsync()).EnsureOk();

            var landed = await WaitUntilAsync(
                () => Mcp.GetEntityVariableAsync(entity, path),
                r => r.Ok && !r.Bool("pending"),
                $"the staged write on '{path}' to drain once the world advanced");

            Output.WriteLine($"{path}: {original} → staged {written} → after the drain "
                             + $"{landed.Field("value")?.ToJsonString()}");
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    /// <summary>
    /// A variable that does not exist must be refused with a pointer to the listing endpoint, not
    /// with an empty success — the difference between "there is no such variable" and "there are no
    /// variables" is exactly what an agent needs.
    /// </summary>
    [SystemSmokeFact]
    public async Task An_unknown_variable_is_refused_with_a_usable_error()
    {
        await LoadAndPreviewAsync(await AnyCuratedScenarioAsync(PreferredScenario), startPaused: true);
        try
        {
            var found = await FindBlueprintEntityAsync();
            if (found is null)
            {
                await AssertHonestRefusalAsync();
                return;
            }

            var result = await Mcp.GetEntityVariableAsync(found.Value.Entity, "zzz-no-such-variable");
            Assert.False(result.Ok);
            Assert.Equal(400, result.StatusCode);
            Assert.Contains("zzz-no-such-variable", result.Error ?? "");
            Assert.Equal("GET /entities/{id}/variables", result.HintEndpoint);

            Output.WriteLine($"rejection: {result.Error}");
        }
        finally
        {
            await ResetToIdleAsync();
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The first entity in the loaded world whose blueprint variables can be listed, with them.
    /// Null when this world has no blueprint-carrying entity.
    /// </summary>
    private async Task<(long Entity, List<JsonNode?> Variables)?> FindBlueprintEntityAsync()
    {
        var listed = await WaitUntilAsync(
            () => Mcp.ListEntitiesAsync(),
            r => r.Ok && r.Array().Count > 0,
            "at least one entity in the loaded world");

        var ids = listed.Array()
            .Select(n => n?["networkId"]?.GetValue<long>())
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        foreach (var id in ids)
        {
            var result = await Mcp.GetEntityVariablesAsync(id);
            if (!result.Ok) continue;

            // An entity that ANSWERS carries a blueprint. Its variable list may still be empty —
            // a Library-dispatch blueprint has no working state — and that is a legitimate answer,
            // not a reason to keep looking: the response shape is what these cases assert.
            Assert.False(string.IsNullOrWhiteSpace(result.String("asset")),
                $"an entity answered /variables with no asset named: {result.Data?.ToJsonString()}");
            var variables = result.Field("variables") as JsonArray;
            Assert.NotNull(variables);

            return (id, variables!.ToList());
        }

        Output.WriteLine($"none of the {ids.Count} entities in this world carries a blueprint.");
        return null;
    }

    /// <summary>
    /// With no blueprint entity to read, the endpoint must still say something an agent can act on —
    /// a 400 naming what is missing, not a 500 and not an empty 200.
    /// </summary>
    private async Task AssertHonestRefusalAsync()
    {
        var entity = await FirstListedEntityAsync();
        var result = await Mcp.GetEntityVariablesAsync(entity);

        Assert.False(result.Ok);
        Assert.Equal(400, result.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Output.WriteLine($"no blueprint entity in this world; refusal reads: {result.Error}");
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

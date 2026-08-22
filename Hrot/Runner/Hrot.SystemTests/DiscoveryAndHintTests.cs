using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// Smoke cases for MCP-extensions slice ① (task <c>MX6</c>): behaviour discovery (<c>MX4a</c>),
/// breakpoint-type discovery (<c>MX7</c>) and self-describing errors (<c>MX8</c>).
///
/// <para><b>What the slice is FOR, and therefore what these assert.</b> The three pieces exist to
/// close one loop — <b>author → err → hint → discover → retry</b>. So the cases do not merely check
/// that two endpoints answer; the last one walks the whole loop: post a condition with a bogus
/// <c>$type</c>, follow the <c>hint</c> the rejection carries, read a real arm from the endpoint it
/// names, and post that instead. ⭐ If the loop closes, the slice did its job.</para>
/// </summary>
[Trait("Category", "SystemSmoke")]
[Trait("Category", "McpExtensions")]
public sealed class DiscoveryAndHintTests : SystemTestBase
{
    public DiscoveryAndHintTests(EditorProcessFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    // ── MX7 — breakpoint-type discovery ────────────────────────────────────────

    [SystemSmokeFact]
    public async Task Breakpoint_types_lists_the_closed_condition_union_with_schemas()
    {
        var arms = (await Mcp.GetBreakpointTypesAsync()).EnsureOk().Array();

        // The union is closed by construction — 12 [JsonDerivedType] arms on SearchPredicateDto.
        // Asserting the count pins the contract: an arm added or lost shows up here.
        Assert.Equal(12, arms.Count);

        var discriminators = arms.Select(a => a?["$type"]?.GetValue<string>()).ToList();
        Assert.Contains("PropertyMatch", discriminators);
        Assert.Contains("Compound", discriminators);
        Assert.Contains("BlueprintVariable", discriminators);

        var propertyMatch = arms.First(a => a?["$type"]?.GetValue<string>() == "PropertyMatch")!;
        var properties = propertyMatch["paramSchema"]?["properties"];
        Assert.NotNull(properties);

        // The three that make PropertyMatch authorable: which component, which field path, and the
        // operator's allowed values.
        Assert.NotNull(properties!["ComponentType"]);
        Assert.NotNull(properties["PropertyPath"]);
        Assert.NotNull(properties["Operator"]?["enum"]);

        // PropertyPath is marked by the EXISTING [PropertyPathPicker] attribute — surfacing it is
        // what tells an agent this is a path to discover, not free text.
        Assert.Equal("propertyPath", properties["PropertyPath"]?["picker"]?.GetValue<string>());

        Output.WriteLine($"arms: {string.Join(", ", discriminators)}");
    }

    [SystemSmokeFact]
    public async Task A_nested_predicate_is_described_by_reference_not_inlined()
    {
        var arms = (await Mcp.GetBreakpointTypesAsync()).EnsureOk().Array();
        var compound = arms.First(a => a?["$type"]?.GetValue<string>() == "Compound")!;

        // Compound holds a list of predicates — including, potentially, more Compounds. Inlining
        // would recurse forever, so the schema names the union by reference.
        var conditions = compound["paramSchema"]?["properties"]?["Conditions"];
        Assert.Equal("array", conditions?["type"]?.GetValue<string>());
        Assert.Equal("SearchPredicateDto", conditions?["items"]?["$ref"]?.GetValue<string>());
    }

    // ── MX4a — behaviour discovery ─────────────────────────────────────────────

    [SystemSmokeFact]
    public async Task Behaviors_are_discoverable_with_their_parameter_schemas()
    {
        var behaviors = (await Mcp.GetBehaviorsAsync()).EnsureOk().Array();
        Assert.NotEmpty(behaviors);

        foreach (var behavior in behaviors)
        {
            // Every entry must be authorable: an id to name, and a schema (possibly empty) to fill.
            Assert.False(string.IsNullOrWhiteSpace(behavior?["id"]?.GetValue<string>()));
            Assert.Equal("object", behavior?["paramSchema"]?["type"]?.GetValue<string>());
        }

        Output.WriteLine($"{behaviors.Count} registered behaviours: " +
                         string.Join(", ", behaviors.Take(8).Select(b => b?["id"]?.GetValue<string>())));
    }

    [SystemSmokeFact]
    public async Task Behaviors_can_be_filtered_to_what_an_entity_may_run()
    {
        await LoadAndPreviewAsync("hill-attack", startPaused: true);

        var entity = (await Mcp.ListEntitiesAsync()).EnsureOk().Array()
            .Select(e => e?["networkId"]?.GetValue<long>())
            .First(id => id is not null)!.Value;

        var forEntity = await Mcp.GetBehaviorsAsync(entityId: entity);
        Assert.True(forEntity.Ok, forEntity.Error);

        // The entity-keyed list is the mission-task combo's list, so it is a SUBSET of everything
        // registered — that relationship is the property worth pinning, not an exact membership.
        var all = (await Mcp.GetBehaviorsAsync()).EnsureOk().Array().Count;
        Assert.True(forEntity.Array().Count <= all,
            $"entity {entity} offered {forEntity.Array().Count} behaviours, more than the {all} registered");

        Output.WriteLine($"entity {entity}: {forEntity.Array().Count} of {all} behaviours");
    }

    /// <summary>
    /// An unknown entity is reported as a mistake about the ID — not answered with an empty list.
    ///
    /// <para>⚠ The mission service returns EMPTY for an unknown id, which is right for a UI combo and
    /// wrong over HTTP: "no such entity" and "this entity can do nothing" would be the same response,
    /// and an agent would take the wrong lesson. So the id is resolved at the boundary, and the hint
    /// points at <c>/entities</c> — where the actual mistake was made — rather than at the behaviour
    /// catalog.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task An_unknown_entity_asking_for_behaviors_is_told_where_to_look()
    {
        var result = await Mcp.GetBehaviorsAsync(entityId: 999_999_999L);

        Assert.False(result.Ok);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("GET /entities", result.HintEndpoint);
    }

    // ── MX8 — self-describing errors ───────────────────────────────────────────

    [SystemSmokeFact]
    public async Task A_bad_condition_carries_a_hint_naming_the_discovery_endpoint()
    {
        var bad = new JsonObject { ["$type"] = "NoSuchPredicateArm" };

        var rejected = await Mcp.AddBreakpointAsync(bad);

        Assert.False(rejected.Ok);
        Assert.Equal("GET /breakpoint-types", rejected.HintEndpoint);
        Assert.False(string.IsNullOrWhiteSpace(rejected.HintWhy));

        // The prose stays for humans; the hint is the machine-readable half, not a replacement.
        Assert.Contains("condition", rejected.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [SystemSmokeFact]
    public async Task The_back_filled_hints_reach_older_endpoints_too()
    {
        var unknownEntity = await Mcp.GetEntityAsync(999_999_999L);
        Assert.False(unknownEntity.Ok);
        Assert.Equal("GET /entities", unknownEntity.HintEndpoint);

        var unknownBaseline = await Mcp.DiffCompareAsync("no-such-baseline");
        Assert.False(unknownBaseline.Ok);
        Assert.Equal("POST /diff/capture", unknownBaseline.HintEndpoint);
    }

    [SystemSmokeFact]
    public async Task A_successful_call_carries_no_hint()
    {
        var status = (await Mcp.GetStatusAsync()).EnsureOk();

        // A hint on a success would be noise an agent has to learn to ignore.
        Assert.Null(status.Hint);
    }

    // ── the loop the whole slice exists to close ───────────────────────────────

    /// <summary>
    /// <b>author → err → hint → discover → retry</b>, walked end to end with no prior knowledge of
    /// the condition vocabulary. This is the slice's actual claim: an agent that gets it wrong can
    /// find out how to get it right, from the error alone.
    /// </summary>
    [SystemSmokeFact]
    public async Task An_agent_can_recover_from_a_bad_condition_using_only_the_hint()
    {
        await LoadAndPreviewAsync("hill-attack", startPaused: true);

        // ① Author blind, and get it wrong.
        var rejected = await Mcp.AddBreakpointAsync(new JsonObject { ["$type"] = "TotallyMadeUp" });
        Assert.False(rejected.Ok);

        // ② The rejection says where to look — machine-readable, no prose parsing.
        var seeEndpoint = rejected.HintEndpoint;
        Assert.Equal("GET /breakpoint-types", seeEndpoint);

        // ③ Follow it, and pick an arm plus its schema.
        var arms = (await Mcp.GetBreakpointTypesAsync()).EnsureOk().Array();
        var lifecycle = arms.First(a => a?["$type"]?.GetValue<string>() == "Lifecycle")!;
        var schema = lifecycle["paramSchema"]!["properties"]!;

        // The schema told us IdentifierType is an enum and what its values are.
        var identifierValues = schema["IdentifierType"]!["enum"]!.AsArray()
            .Select(v => v!.GetValue<string>()).ToList();
        Assert.Contains("NameSubstring", identifierValues);

        // ④ Retry with a condition shaped by what discovery returned.
        //
        // ⚠ The target deliberately matches NOTHING. This case is about the condition being
        // ACCEPTED, not about it firing — and a breakpoint that fires PAUSES the simulation, which
        // outlives the breakpoint's removal and strands the shared editor. An earlier version used
        // "Bradley", which hill-attack really contains: it tripped during a later case's scenario
        // load and left the cluster unable to reach OperatingEdit.
        var condition = new JsonObject
        {
            ["$type"] = "Lifecycle",
            ["IdentifierType"] = "NameSubstring",
            ["TargetValue"] = "zzz-no-such-entity-name",
        };
        var accepted = await Mcp.AddBreakpointAsync(condition, name: "mx6-recovered");

        Assert.True(accepted.Ok, $"the recovered condition was still rejected: {accepted.Error}");
        var id = accepted.String("breakpointId");
        Assert.False(string.IsNullOrWhiteSpace(id));

        Output.WriteLine($"recovered blind → {seeEndpoint} → Lifecycle → breakpoint {id}");
        (await Mcp.RemoveBreakpointAsync(id!)).EnsureOk();
    }
}

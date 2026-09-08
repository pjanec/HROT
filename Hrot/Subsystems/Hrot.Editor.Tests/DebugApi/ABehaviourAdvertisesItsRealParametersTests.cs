#nullable enable
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// <c>CE-224</c> — <c>GET /behaviors</c> must advertise a behaviour's REAL parameters, so an agent (or
/// the editor's Mission panel) can author a task without guessing.
///
/// <para>
/// ⛔⛔ <b>WHY THESE RAILS EXIST IN THIS SHAPE, and why the three that were already green did not
/// catch it.</b> Every behaviour reported <c>paramSchema: {"type":"object","properties":{}}</c> on every
/// host — measured live on two of them — while three rails stayed green:
/// <list type="bullet">
///   <item><c>DiscoveryAndHintTests</c> asserted <c>paramSchema.type == "object"</c> — the
///     <b>envelope</b>, never the contents, with its own comment calling an empty schema a pass.</item>
///   <item><c>BehaviorRegistryTests</c> asserted the curated DTO type wins a precedence contest, using a
///     <b>fake</b> registration — so it proved the precedence works IF USED, never that it is used.</item>
///   <item><c>DebugApiCompositionTests</c> only named the field in a comment.</item>
/// </list>
/// ⇒ ⭐⭐ so the rails below assert a <b>NAMED behaviour has NAMED parameters</b>, resolved from a registry
/// built by the <b>production</b> loader — not from a fake, and not "is an object".
/// </para>
///
/// <para>
/// ⚠ <c>R-142</c> checked: <c>DtoJsonSchemaExtractor</c> had <b>no</b> suite of its own — measured,
/// <c>scripts/find.sh DtoJsonSchemaExtractor</c> returns three hits, two production call sites and the
/// declaration, none in a test file.
/// </para>
/// </summary>
public sealed class ABehaviourAdvertisesItsRealParametersTests
{
    /// <summary>
    /// The production loader, verbatim — <c>CgfSubsystem</c>'s <c>behavior-registry</c> boot step calls
    /// exactly this. It scans the compile-time <c>Hrot.AI.Behaviors</c> assembly through
    /// <c>BlueprintRegistrarScanner</c>, which is what invokes <c>CgfCuratedBehaviorRegistrar</c>
    /// reflectively.
    /// </summary>
    private static BehaviorRegistry LoadProductionRegistry()
    {
        var registry = new BehaviorRegistry();
        CgfBehaviorSetup.LoadFromAiAssembly(registry);
        return registry;
    }

    private static JsonObject SchemaFor(BehaviorRegistry registry, string behaviourName)
    {
        Assert.True(registry.TryGetId(behaviourName, out int id),
            $"Behaviour '{behaviourName}' is not registered at all — the loader changed, not just the schema.");
        Assert.True(registry.TryGetDefinition(id, out var definition) && definition is not null,
            $"Behaviour '{behaviourName}' has an id but no definition.");

        return DtoJsonSchemaExtractor.ExtractParams(definition!.ParamsDtoType);
    }

    private static string[] PropertyNames(JsonObject schema)
        => (schema["properties"] as JsonObject)?.Select(kv => kv.Key).ToArray() ?? System.Array.Empty<string>();

    /// <summary>
    /// ⭐ THE ONE THAT MATTERS. <c>MoveToLocation</c> must advertise the four fields its params struct
    /// actually has. Named, not counted: a count assertion would pass against the wrong four.
    ///
    /// <para>
    /// ⚠ Inverse-edit red-proof: drop the field loop from
    /// <c>DtoJsonSchemaExtractor.PublicWritableMembers</c> and this fails with an empty list — which is
    /// exactly the state the live API was in before <c>CE-224</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void MoveToLocationAdvertisesItsFourParameters()
    {
        JsonObject schema = SchemaFor(LoadProductionRegistry(), "MoveToLocation");

        Assert.Equal(
            new[] { "X", "Y", "Speed", "ArrivalRadius" },
            PropertyNames(schema));
    }

    /// <summary>
    /// The types must be right too, or an agent sends a string where a number belongs and the resolver
    /// silently writes a zero — the failure mode <c>R-132</c> records (a platoon that drove to the origin).
    /// </summary>
    [Fact]
    public void TheAdvertisedParameterTypesMatchTheStruct()
    {
        var props = (JsonObject)SchemaFor(LoadProductionRegistry(), "MoveToLocation")["properties"]!;

        Assert.Equal("number", (string?)props["X"]!["type"]);
        Assert.Equal("number", (string?)props["Y"]!["type"]);
        Assert.Equal("number", (string?)props["Speed"]!["type"]);
        Assert.Equal("number", (string?)props["ArrivalRadius"]!["type"]);
    }

    /// <summary>
    /// ⭐⭐ Not one behaviour by luck. Every behaviour that HAS a params DTO must advertise something —
    /// this is what turns the fix from "MoveToLocation works" into a property of the endpoint.
    ///
    /// <para>
    /// ⚠ Deliberately scoped to behaviours with a non-null <c>ParamsDtoType</c>. Most registered
    /// behaviours are generated ones that never set it, and that gap is its own finding
    /// (<c>CE-226</c>) — asserting over ALL behaviours here would either fail for a reason this rail
    /// does not own, or have to be weakened into vacuity.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryBehaviourThatHasAParamsDtoAdvertisesAtLeastOneParameter()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        var withDto = registry.GetRegisteredNames()
            .Where(n => registry.TryGetId(n, out int id)
                     && registry.TryGetDefinition(id, out var d)
                     && d?.ParamsDtoType is not null)
            .ToArray();

        Assert.NotEmpty(withDto);   // anti-vacuity: an empty set would make the loop below pass for free

        foreach (string name in withDto)
            Assert.True(PropertyNames(SchemaFor(registry, name)).Length > 0,
                $"Behaviour '{name}' declares a params DTO but advertises no parameters.");
    }

    /// <summary>
    /// A behaviour with genuinely no parameters still returns the envelope rather than null, so a caller
    /// never has to tell "no params" from "unknown". <c>Idle</c> is the real instance of that case.
    /// </summary>
    [Fact]
    public void ABehaviourWithNoParamsStillReturnsAnEmptyObjectSchema()
    {
        JsonObject schema = SchemaFor(LoadProductionRegistry(), "Idle");

        Assert.Equal("object", (string?)schema["type"]);
        Assert.Empty(PropertyNames(schema));
    }

    /// <summary>
    /// ⚠ The complement, and the reason the fix is a widening rather than a swap: the predicate DTOs are
    /// ordinary classes with PROPERTIES, they worked before, and they must keep working. Reading fields
    /// only — the mirror-image mistake — would break <c>GET /breakpoint-types</c> instead.
    /// </summary>
    [Fact]
    public void PropertyBasedDtosStillWork()
    {
        JsonArray arms = DtoJsonSchemaExtractor.ExtractPredicateUnion();

        Assert.NotEmpty(arms);
        Assert.Contains(arms, arm =>
            PropertyNames((JsonObject)arm!["paramSchema"]!).Length > 0);
    }
}

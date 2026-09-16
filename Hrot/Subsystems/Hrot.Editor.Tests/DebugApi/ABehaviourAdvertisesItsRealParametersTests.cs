#nullable enable
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// <c>CE-224</c> → <c>CE-235</c> — <c>GET /behaviors</c> must advertise the shape a caller WRITES when
/// assigning a behaviour, so an agent (or the editor's Mission panel) can author a task without guessing.
///
/// <para>
/// ⛔⛔ <b><c>CE-235</c> REWROTE THIS SUITE, AND THE REASON MATTERS MORE THAN THE FIX.</b> The original
/// version asserted <c>MoveToLocation</c> advertises <c>X, Y, Speed, ArrivalRadius</c> — the fields of
/// <c>CgfNodes.MoveToLocationParams</c>, the <b>blittable blackboard struct</b>. That made the rails
/// green while the endpoint published an <i>engine-internal memory layout</i> as a public contract.
/// 🔒 User ruling, <c>2026-09-08</c>: <i>"the behavior spec from scenario or from mcp server or from
/// wherever always comes with json/dto only … blackboard layout is internal and there is a translator
/// that converts the json dto into blackboard."</i>
/// 📄 <c>Behavior_Parameter_Resolver_Detailed_Design.md</c> §3.2 — only the <b>authored DTO</b> is
/// authored; the usable params are hot-path input the resolver writes.
/// </para>
///
/// <para>
/// 📐 <b>What the old rails could not see, measured on <c>FireAtTarget</c>:</b> the blackboard struct is
/// <c>{TargetPacked, MaxRounds, CooldownSeconds, RoundsFired}</c> while the JSON the resolver parses is
/// <c>{TargetNetworkId, MaxRounds, CooldownSeconds}</c>. So the endpoint advertised a resolved handle and
/// a runtime OUTPUT counter — neither settable by a caller — and omitted the only key that aims the
/// weapon. An agent following that schema could not fire. <see cref="TheSchemaNeverLeaksARuntimeOutputField"/>
/// is the rail that now fails if that returns.
/// </para>
/// </summary>
public sealed class ABehaviourAdvertisesItsRealParametersTests
{
    /// <summary>
    /// The production loader, verbatim — <c>CgfSubsystem</c>'s <c>behavior-registry</c> boot step calls
    /// exactly this. It scans the compile-time <c>Hrot.AI.Behaviors</c> assembly through
    /// <c>BlueprintRegistrarScanner</c> (which invokes <c>CgfCuratedBehaviorRegistrar</c> reflectively),
    /// and since <c>CE-235</c> also binds each <c>[BehaviorContract]</c> DTO as the authored contract.
    /// ⭐ Using the real loader is the point: a fake registry proves the precedence rules work IF USED,
    /// never that they ARE used — which is how <c>CE-224</c> stayed hidden.
    /// </summary>
    private static BehaviorRegistry LoadProductionRegistry()
    {
        var registry = new BehaviorRegistry();
        CgfBehaviorSetup.LoadFromAiAssembly(registry);
        return registry;
    }

    private static BehaviorDefinition Definition(BehaviorRegistry registry, string behaviourName)
    {
        Assert.True(registry.TryGetId(behaviourName, out int id),
            $"Behaviour '{behaviourName}' is not registered at all — the loader changed, not just the schema.");
        Assert.True(registry.TryGetDefinition(id, out var definition) && definition is not null,
            $"Behaviour '{behaviourName}' has an id but no definition.");
        return definition!;
    }

    private static JsonObject SchemaFor(BehaviorRegistry registry, string behaviourName)
        => DtoJsonSchemaExtractor.ExtractParams(Definition(registry, behaviourName));

    private static string[] PropertyNames(JsonObject schema)
        => (schema["properties"] as JsonObject)?.Select(kv => kv.Key).ToArray() ?? System.Array.Empty<string>();

    /// <summary>
    /// ⭐ THE ONE THAT MATTERS. <c>MoveToLocation</c> advertises its AUTHORED keys — the geo pair that is
    /// the canonical form, plus the Cartesian escape hatch the user ruled should stay discoverable.
    ///
    /// <para>⚠ Inverse-edit red-proof: point <c>DtoJsonSchemaExtractor.ExtractParams</c> back at
    /// <c>definition.BlackboardLayoutType</c> and this fails with the blackboard struct's four fields —
    /// exactly the state <c>CE-224</c> left the endpoint in.</para>
    /// </summary>
    [Fact]
    public void MoveToLocationAdvertisesItsAuthoredJsonKeys()
    {
        string[] advertised = PropertyNames(SchemaFor(LoadProductionRegistry(), "MoveToLocation"));

        Assert.Contains("TargetLat", advertised);
        Assert.Contains("TargetLon", advertised);
        Assert.Contains("Speed", advertised);
        Assert.Contains("ArrivalRadius", advertised);

        // 🔒 User ruling 2026-09-08: cartesian is origin-dependent so geo is canonical, "but for purpose
        //    of ai driven development cartesian is much easier … so i would keep it". Advertised, not hidden.
        Assert.Contains("X", advertised);
        Assert.Contains("Y", advertised);
    }

    /// <summary>
    /// ⭐⭐⭐ THE DEFECT THE OLD SUITE COULD NOT EXPRESS. The schema must not name a field that exists only
    /// inside the blackboard. <c>RoundsFired</c> is a runtime counter the executor writes and
    /// <c>TargetPacked</c> is a resolved <c>Entity</c> handle; neither is settable over the wire, and the
    /// JSON DTO carries neither.
    ///
    /// <para>⚠ Inverse-edit red-proof: repoint the extractor at the layout type and this fails on both
    /// names at once.</para>
    /// </summary>
    [Fact]
    public void TheSchemaNeverLeaksARuntimeOutputField()
    {
        string[] advertised = PropertyNames(SchemaFor(LoadProductionRegistry(), "FireAtTarget"));

        Assert.DoesNotContain("RoundsFired", advertised);
        Assert.DoesNotContain("TargetPacked", advertised);
        Assert.Contains("TargetNetworkId", advertised);   // and the key that DOES aim it is present
    }

    /// <summary>
    /// The types must be right too, or an agent sends a string where a number belongs and the resolver
    /// silently writes a zero — the failure mode <c>R-132</c> records (a platoon that drove to the origin).
    /// </summary>
    [Fact]
    public void TheAdvertisedParameterTypesAreNumeric()
    {
        var props = (JsonObject)SchemaFor(LoadProductionRegistry(), "MoveToLocation")["properties"]!;

        Assert.Equal("number", (string?)props["TargetLat"]!["type"]);
        Assert.Equal("number", (string?)props["TargetLon"]!["type"]);
        Assert.Equal("number", (string?)props["Speed"]!["type"]);
        Assert.Equal("number", (string?)props["ArrivalRadius"]!["type"]);
    }

    /// <summary>
    /// ⭐⭐ THE STRUCTURAL GUARANTEE, not one behaviour by luck: for EVERY behaviour that declares an
    /// authored contract, what the endpoint advertises IS that contract — entry for entry. This is what
    /// turns the fix from "MoveToLocation works" into a property of the endpoint.
    ///
    /// <para>
    /// ⚠ <b>Correspondence, deliberately, not "at least one".</b> 📐 The first draft asserted
    /// non-emptiness and reddened on <c>JoinFormation</c>, whose <c>[BehaviorContract]</c> DTO is
    /// <i>deliberately</i> empty — its own doc says <i>"currently parameterless; the contract exists to
    /// anchor the behavior ID and category."</i> ⭐ So an empty schema is the TRUTH there, and the rail
    /// was wrong rather than the code. Equality is both stronger (it catches a wrong non-empty answer,
    /// which non-emptiness cannot) and honest about the parameterless case.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryBehaviourWithAnAuthoredContractAdvertisesExactlyIt()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        var authored = registry.GetRegisteredNames()
            .Select(n => (Name: n, Def: Definition(registry, n)))
            .Where(x => x.Def.JsonParamsDtoType is not null)
            .ToArray();

        Assert.NotEmpty(authored);   // anti-vacuity: an empty set would make the loop pass for free

        bool anyCarriedMembers = false;
        foreach (var (name, def) in authored)
        {
            string[] advertised = PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def));
            string[] contract   = PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def.JsonParamsDtoType));

            Assert.True(advertised.SequenceEqual(contract),
                $"Behaviour '{name}' advertises [{string.Join(", ", advertised)}] " +
                $"but its authored contract is [{string.Join(", ", contract)}].");

            anyCarriedMembers |= contract.Length > 0;
        }

        // ⭐ Second anti-vacuity guard: equality alone would pass if EVERY contract were empty.
        Assert.True(anyCarriedMembers, "no authored contract carried a single member — the loader is broken.");
    }

    /// <summary>
    /// ⭐⭐⭐ THE SEPARATION ITSELF, asserted so it cannot quietly collapse back. The curated behaviours
    /// that DIVERGE carry two DIFFERENT types — §3.2's "two shapes on divergence" — and the public one is
    /// never the blittable struct.
    /// </summary>
    [Theory]
    [InlineData("MoveToLocation")]
    [InlineData("FireAtTarget")]
    public void ADivergentBehaviourKeepsItsAuthoredAndBlackboardTypesDistinct(string behaviourName)
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), behaviourName);

        Assert.NotNull(def.JsonParamsDtoType);
        Assert.NotNull(def.BlackboardLayoutType);
        Assert.NotEqual(def.JsonParamsDtoType, def.BlackboardLayoutType);

        // The authored one is a heap class a JSON serializer can round-trip; the internal one is a
        // blittable struct projected over raw bytes.
        Assert.True(def.JsonParamsDtoType!.IsClass,      "the authored contract must be a JSON-serializable class");
        Assert.True(def.BlackboardLayoutType!.IsValueType, "the blackboard layout must stay a blittable struct");
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
    /// ⚠ The complement, and the reason <c>CE-224</c>'s extractor fix is a widening rather than a swap:
    /// the predicate DTOs are ordinary classes with PROPERTIES, they worked before, and they must keep
    /// working. Reading fields only — the mirror-image mistake — would break <c>GET /breakpoint-types</c>.
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

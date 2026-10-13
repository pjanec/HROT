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
    /// <c>BlueprintRegistrarScanner</c> (which invokes the generated <c>CuratedBehaviorRegistrar</c> reflectively),
    /// and since <c>CE-235</c> also binds each <c>[Behavior(Params = …)]</c> DTO as the authored contract.
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
    /// non-emptiness and reddened on <c>JoinFormation</c>, whose <c>[Behavior(Params = …)]</c> DTO is
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

        // The authored one is what a JSON serializer round-trips (CE-2023 ③, "S8n": now a struct, so a host can bind it —
        // it was pinned here as a class); the internal one is a blittable struct projected over raw bytes.
        Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(
            System.Activator.CreateInstance(def.JsonParamsDtoType!)), def.JsonParamsDtoType!));
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
    /// <summary>
    /// ⭐ CE-2131 (CE-2128 ①) — <c>FollowRoute</c> advertises every key its callers write: the route entity, the speed and the
    /// loop flag (the behaviour's typed resolver reads all three, CE-2145). ⚠ Red-proof: drop <c>Speed</c> from
    /// <c>FollowRouteParamsJsonDto</c> → this fails naming it.
    /// </summary>
    [Fact]
    public void CE2131_FollowRouteAdvertisesTheKeysItsCallersWrite()
    {
        var keys = PropertyNames(SchemaFor(LoadProductionRegistry(), "FollowRoute"));

        Assert.Contains("RouteEntityId", keys);   // the schema lists C# member names; JSON matching is case-insensitive
        Assert.Contains("Speed", keys);
        Assert.Contains("Loop", keys);
    }

    /// <summary>
    /// ⭐ <c>CE-2132</c> (A2, docs/DESIGN_Orders_Params_Entity_Classes.md §6) — the four multi-variable library trees that no
    /// caller sends JSON (measured: no scenario, test or asset params string names their variables) name <c>NoParams</c>: their
    /// variables are baked tuning for their own nodes, not a contract. 🔴 Red-proof: drop <c>ParamsDtoTypeId</c> from one asset
    /// → it publishes its generated mirror again and this fails naming it.
    /// </summary>
    [Theory]
    [InlineData("Idle")]
    [InlineData("WanderMilitary")]
    [InlineData("JoinFormation", false)]
    public void CE2131_ABehaviourThatTakesNothing_NamesNoParams(string behaviour, bool takesNothing = true)
    {
        // ⭐ CE-2131 (A1, §6) — the empty anchor DTOs are gone: a behaviour that takes nothing names NoParams (rule P1).
        //   JoinFormation is the control: it takes params and names the struct its identity parse fills (CE-2134 BEH003).
        //   (Ambush / InfantryCombat / ConvoyEscort are FDP examples the Editor host declares; ParamsDtoRegistryTests covers them.)
        //   🔴 Red-proof: point Idle's [Behavior(Params=…)] back at a DTO → this fails naming Idle.
        var registry = LoadProductionRegistry();
        Assert.True(registry.TryGetId(behaviour, out int id) && registry.TryGetDefinition(id, out var d) && d is not null,
            $"{behaviour} must be registered");
        registry.TryGetDefinition(id, out var def);
        Assert.Equal(takesNothing, def!.JsonParamsDtoType == typeof(Hrot.Map.Definitions.Behavior.NoParams));
    }

    [Theory]
    [InlineData("BasicInfantrySop")]
    [InlineData("BasicSquadSop")]
    [InlineData("FiringPosition")]
    [InlineData("Flank")]
    public void CE2132_ALibraryTreeNoCallerParameterises_NamesNoParams(string behaviour)
    {
        var registry = LoadProductionRegistry();
        Assert.True(registry.TryGetId(behaviour, out int id) && registry.TryGetDefinition(id, out var def) && def is not null,
            $"{behaviour} must be registered");
        registry.TryGetDefinition(id, out var d);
        Assert.Equal(typeof(Hrot.Map.Definitions.Behavior.NoParams), d!.JsonParamsDtoType);
    }

    /// <summary>⭐ <c>CE-2132</c> — <c>DangerCrossing</c> publishes its resolver's flat contract, the blueprint twin's
    /// <c>{Objective, WalkSpeed, RushSpeed, CrossWithin}</c>, not the <c>DangerCrossingConfig</c> block its nodes bind
    /// (<c>Params.sensor</c> …). 🔴 Red-proof: drop the <c>[Behavior]</c> declaration → it publishes the config struct.</summary>
    [Fact]
    public void CE2132_DangerCrossing_PublishesItsFlatContract_NotItsBlock()
    {
        var registry = LoadProductionRegistry();
        Assert.True(registry.TryGetId("DangerCrossing", out int id) && registry.TryGetDefinition(id, out var d) && d is not null);
        registry.TryGetDefinition(id, out var def);
        Assert.Equal(typeof(Hrot.AI.Behaviors.Brains.DangerCrossingParams), def!.JsonParamsDtoType);
        var keys = PropertyNames(SchemaFor(registry, "DangerCrossing"));
        Assert.Contains("Objective", keys);
        Assert.Contains("CrossWithin", keys);
    }

    // ── CE-2130 (docs/DESIGN_Orders_Params_Entity_Classes.md §1 P1–P2, §6 R0) ─────────────────────────────────────────────
    //   The authoring rules as a conformance rail: every registered behaviour names exactly ONE params DTO (its public JSON
    //   contract), and that DTO is a NAMED type — never a generator's mirror of the behaviour's own variables (which is what
    //   makes the JSON nest under variable names, `{"sentry":{…}}`). Today's violators sit in an allow-list that may only
    //   SHRINK: a new violator fails, and an entry that no longer violates fails too ("remove it").

    /// <summary>⭐ CE-2130 — today's violators of P1 / P2, by behaviour name. ⛔ Never add to this list; the tasks of the
    /// orders programme remove entries (CE-2131: the empty DTOs; CE-2132: the multi-Input assets; CE-2133: the named DTO).</summary>
    internal static readonly string[] ParamsContractAllowList =
    {
        // measured 2026-10-12 at the first run of the rail: 48 behaviours
        // CE-2133 (B1, 2026-10-12) removed 8: one struct Input now IS the params DTO (FallBack, HullDownAttackRun,
        //   PlatoonHillAttack, Sentry, T11_Aliasing, T31_ComposedAiPrimitive, T34_ComposedAiPrimitiveCondition, TakeCover)
        // CE-2135 (B3a, 2026-10-12) removed 2: T32_ComposedGeneratedBlueprint, T33_ComposedParamBlueprint now name NoParams
        // CE-2132 (A2, 2026-10-12) removed 4: BasicInfantrySop, BasicSquadSop, FiringPosition, Flank now name NoParams (no
        //   caller sends them JSON; their variables are baked tuning)
        // CE-2135 (B3b prep, 2026-10-12) removed 15 that store nothing: BTreeRenderShowcase, CombatShowcase, SampleScout, SampleGuard,
        //   HsmShowcase, HsmTwoChannelRegionsDemo, HsmOrthogonalRegions (its one variable is State), T01–T08 now name NoParams
        // CE-2132 (A2, 2026-10-12, after CE-3182) removed DangerCrossing: it publishes DangerCrossingParams (its resolver's contract)
        // R-2006 (P9, 2026-10-13) removed PlatoonHillAttackBp: the asset is retired
        "BTreeCuratedBindingDemo", "BlueprintBehaviourDemo", "DangerCrossingBp",
        "Demo_Advance", "Demo_Engage", "Demo_MissionPlan", "Demo_Retreat",
        "Demo_TakeCover", "Demo_TaskChain",
        "HsmResolverDemo", "OperatorControl", "T09_BlackboardManaged", "T10_MultiAction", "T20_MultiStateful", "T35_SharedWorkingState",
        "T39_TwoDistinctPrimitives", "T40_BehaviorResolverAsset",
    };

    /// <summary>⭐ CE-2130 — true when <paramref name="t"/> is a generator's mirror of a behaviour's own variables rather
    /// than a named params DTO: a BTree asset's Inputs struct (emitted into <c>Hrot.AI.Behaviors.Trees</c>) or a blueprint
    /// behaviour's nested <c>Params</c> (inside its generated <c>*_Bp</c> class).</summary>
    internal static bool IsGeneratedMirror(System.Type t)
        => t.Namespace == "Hrot.AI.Behaviors.Trees"
           || (t.Name == "Params" && t.DeclaringType is { } d && d.Name.EndsWith("_Bp", System.StringComparison.Ordinal));

    /// <summary>⭐ CE-2130 — every behaviour in <paramref name="registry"/> that breaks P1 (no params DTO) or P2 (the DTO
    /// is a generated mirror), with the reason.</summary>
    internal static System.Collections.Generic.SortedDictionary<string, string> ParamsContractViolations(BehaviorRegistry registry)
    {
        var bad = new System.Collections.Generic.SortedDictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var name in registry.GetRegisteredNames())
        {
            if (!registry.TryGetId(name, out int id) || !registry.TryGetDefinition(id, out var def) || def is null) continue;
            if (def.JsonParamsDtoType is null) bad[name] = "names no params DTO (P1; one that takes nothing names NoParams)";
            else if (IsGeneratedMirror(def.JsonParamsDtoType))
                bad[name] = $"its params DTO is a generated mirror of its variables, {def.JsonParamsDtoType.Name} (P2: name a DTO; the JSON must be flat)";
        }
        return bad;
    }

    /// <summary>
    /// ⭐⭐ CE-2130 — THE RAIL. Every shipped behaviour follows the params rules, except the allow-listed ones, and every
    /// allow-listed one still breaks them (so the list can only shrink).
    /// <para>⚠ Red-proof: delete any entry from <see cref="ParamsContractAllowList"/> → this fails naming it; add a name
    /// that complies → this fails asking to remove it.</para>
    /// </summary>
    [Fact]
    public void CE2130_EveryBehaviourNamesOneFlatParamsDto_ExceptTheShrinkingAllowList()
    {
        var registry = LoadProductionRegistry();
        Assert.True(registry.GetRegisteredNames().Count > 20,
            "the production loader registered almost nothing — this rail would pass vacuously");

        var violations = ParamsContractViolations(registry);
        var allowed = new System.Collections.Generic.HashSet<string>(ParamsContractAllowList, System.StringComparer.Ordinal);

        var newViolators = violations.Keys.Where(n => !allowed.Contains(n)).Select(n => $"  {n}: {violations[n]}").ToArray();
        Assert.True(newViolators.Length == 0,
            "behaviours break the params rules (docs/DESIGN_Orders_Params_Entity_Classes.md §1) — fix them; never add them "
            + "to the allow-list:\n" + string.Join("\n", newViolators));

        var stale = ParamsContractAllowList.Where(n => !violations.ContainsKey(n)).ToArray();
        Assert.True(stale.Length == 0,
            "these allow-listed behaviours now comply (or are gone) — remove them from ParamsContractAllowList so the list "
            + "keeps shrinking:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>⭐ CE-2130 — the rule itself is not vacuous: a behaviour registered with no params DTO, and one whose DTO is
    /// a generated mirror, are both caught (a permanent red-proof of <see cref="ParamsContractViolations"/>).</summary>
    [Fact]
    public void CE2130_TheRuleCatchesAMissingAndAMirroredParamsDto()
    {
        var registry = new BehaviorRegistry();
        registry.Register("CE2130_NoDto", new BehaviorDefinition { Name = "CE2130_NoDto" });
        var mirror = typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly.GetTypes().First(IsGeneratedMirror);
        registry.Register("CE2130_Mirror", new BehaviorDefinition { Name = "CE2130_Mirror", JsonParamsDtoType = mirror });
        registry.Register("CE2130_Named", new BehaviorDefinition
        {
            Name = "CE2130_Named", JsonParamsDtoType = typeof(Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto),
        });

        var violations = ParamsContractViolations(registry);

        Assert.Contains("CE2130_NoDto", violations.Keys);
        Assert.Contains("CE2130_Mirror", violations.Keys);
        Assert.DoesNotContain("CE2130_Named", violations.Keys);
    }

    // ── CE-2134 (docs/DESIGN_Orders_Params_Entity_Classes.md §5b) — the contract is declared ON THE BEHAVIOUR ───────────

    /// <summary>
    /// ⭐⭐ CE-2134 — every behaviour this host registers that DECLARES a params DTO (<c>[Behavior(Params = …)]</c>, read by the
    /// one scan <see cref="Hrot.Map.Definitions.Behavior.ParamsDtoRegistry"/>) publishes exactly that DTO: the declaration
    /// outranks a generated contract. ⭐ The case it was built for: <c>PlatoonHillAttack</c> (a JSON asset whose curated resolver
    /// parses <c>PlatoonHillAttackParamsJsonDto</c>) published its asset's Input struct (CE-2128 ②).
    /// <para>⚠ Red-proof: restore "a generated contract keeps its type" in <c>BehaviorRegistry.RegisterJsonParamsDtoType</c> ⇒
    /// PlatoonHillAttack publishes <c>PlatoonHillAttackParams</c> and this fails naming it.</para>
    /// </summary>
    [Fact]
    public void CE2134_EveryDeclaredBehaviour_PublishesItsDeclaredDto()
    {
        var registry = LoadProductionRegistry();
        // ⛔ The copy the loader scanned, not the last-loaded one: the hot-reload rails load other copies into this process.
        var declared = Hrot.Map.Definitions.Behavior.ParamsDtoRegistry.Pinning(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);
        var wrong = new System.Collections.Generic.List<string>();
        int checkedCount = 0;
        foreach (var d in declared.Behaviors)
        {
            if (!registry.TryGetId(d.Name, out int id) || !registry.TryGetDefinition(id, out var def) || def is null) continue;
            checkedCount++;
            if (def.JsonParamsDtoType != d.ParamsType)
                wrong.Add($"{d.Name}: declares {d.ParamsType.Name} on {d.DeclaredOn}, publishes {def.JsonParamsDtoType?.Name ?? "nothing"}");
        }
        Assert.True(checkedCount >= 19, $"only {checkedCount} declared behaviours are registered — the scan or the loader changed");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        Assert.Equal(typeof(Hrot.Map.Definitions.Behavior.PlatoonHillAttackParamsJsonDto), Definition(registry, "PlatoonHillAttack").JsonParamsDtoType);
    }

    /// <summary>⭐ CE-2134 — the 25 contracts the DTOs used to carry are all declared on behaviours now (19 curated topologies,
    /// the PlatoonHillAttack resolver, two intent mappers, and the editor's three example behaviours), and the retired
    /// attribute is gone from every loaded assembly.</summary>
    [Fact]
    public void CE2134_TheFormerContractsAreDeclaredOnTheirBehaviours()
    {
        _ = typeof(Hrot.Editor.EditorSubsystem);   // load the editor assembly: it declares the three example behaviours
        var declared = Hrot.Map.Definitions.Behavior.ParamsDtoRegistry.Default;
        string[] expected =
        {
            "AdvanceTo", "Ambush", "BoundingOverwatch", "BreakContact", "CombatPosture", "ConvoyEscort", "DefendArea",
            "DoorLocksmith", "FireAtPoint", "FireAtTarget", "FollowRoute", "HoldStance", "HullDownAttack", "Idle",
            "InfantryCombat", "JoinFormation", "MoveToLocation", "PlatoonHillAttack", "ReactToContact", "SquadHold",
            "SquadMarch", "SuppressAndManeuver", "TrackTarget", "WanderMilitary", "WindowDuel",
        };
        var missing = expected.Where(n => !declared.TryGetBehavior(n, out _)).ToArray();
        Assert.True(missing.Length == 0, "not declared: " + string.Join(", ", missing));
        Assert.True(declared.TryGetBehavior("Ambush", out var ambush) && ambush.DeclaredOn.Contains("Hrot.Editor"),
            "the example behaviours are declared beside the host that registers them");
        Assert.DoesNotContain(System.AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic),
            a => a.GetType("Hrot.Map.Definitions.Behavior.BehaviorContractAttribute") != null);
    }
}

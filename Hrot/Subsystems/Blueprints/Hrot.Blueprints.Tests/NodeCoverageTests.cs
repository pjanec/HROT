using System.Reflection;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Self-enforcing safety net for blueprint node kinds (see task write-up "Build a
/// self-enforcing test safety net for blueprint node kinds").
/// <para>
/// Guard 2 -- pin-schema smoke coverage: <see cref="BuiltInNodeRegistry.GetStaticPins"/> must
/// never throw or return null for any concrete node kind.
/// </para>
/// <para>
/// Guard 3 -- "no untested node kind" compile coverage: every concrete <see cref="Node"/>
/// subtype must either (a) appear in a Blueprint asset that actually compiles+loads through
/// <see cref="BlueprintTestFixture"/>'s real pipeline (Roslyn + ALC), or (b) be listed in
/// <see cref="CompileCoverageExceptions"/> with a documented, reviewed reason. Both lists are
/// reflection-driven against <c>typeof(Node).Assembly</c>, so a newly-added node kind with no
/// fixture and no exception fails <see cref="AllNodeKinds_AreCoveredByACompilingFixtureOrADocumentedException"/>
/// immediately.
/// </para>
/// </summary>
public sealed class NodeCoverageTests
{
    // ── shared reflection helpers ──────────────────────────────────────────

    private static IReadOnlyList<Type> AllConcreteNodeKinds() =>
        typeof(Node).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Node)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    public static IEnumerable<object[]> ConcreteNodeKindTheoryData() =>
        AllConcreteNodeKinds().Select(t => new object[] { t });

    // ========================================================================
    // Guard 2 -- pin-schema smoke coverage
    // ========================================================================

    /// <summary>
    /// For every concrete node kind, <see cref="BuiltInNodeRegistry.GetStaticPins"/> must not
    /// throw and must return a non-null list. Many kinds legitimately have zero static pins
    /// (dynamic kinds enriched later by Stage0_Rehydrate) -- this only guards against an
    /// accidental unhandled kind throwing or falling through to something null.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConcreteNodeKindTheoryData))]
    public void GetStaticPins_NeverThrowsOrReturnsNull_ForEveryConcreteNodeKind(Type nodeType)
    {
        var node = (Node)Activator.CreateInstance(nodeType)!;

        IReadOnlyList<PinSchema>? pins = null;
        var ex = Record.Exception(() => pins = BuiltInNodeRegistry.Instance.GetStaticPins(node));

        Assert.Null(ex);
        Assert.NotNull(pins);
    }

    // ========================================================================
    // Guard 3 -- compile-pipeline coverage
    // ========================================================================

    /// <summary>
    /// Node kinds with NO Stage5_Schedule lowering case at all. Each one wraps a real,
    /// existing FDP primitive (per its own doc comment on <c>Nodes.cs</c>) and has a JSON
    /// discriminator + pin schema + editor palette entry -- but Stage5_Schedule's exec-statement
    /// builder has no <c>case</c> for it, so it falls through to the generic
    /// <c>default: // Unknown impure node kind -- emit BP4004 and skip</c> branch. The graph
    /// still "compiles" (<see cref="CompileResult.Succeeded"/> is true; BP4004 is a *warning*,
    /// not an error) but the node is silently dropped from the emitted IR -- it is a no-op at
    /// runtime. This is a genuine, pre-existing compiler gap, not a test gap; see the write-up.
    /// <para>
    /// Kept intentionally minimal and reviewed. If Stage5_Schedule lowering is implemented for a
    /// kind, <see cref="ExceptedNodes_CurrentlyHitUnimplementedLoweringWarning"/> below will start
    /// FAILING for it (BP4004 will stop being emitted) -- that failure is the signal to remove the
    /// kind from this dictionary and give it real compile-fixture coverage in
    /// <see cref="CoverageAssets"/> instead.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> CompileCoverageExceptions =
        new Dictionary<Type, string>
        {
            [typeof(PartitionElementsNode)] =
                "No Stage5_Schedule case for PartitionElementsNode -- falls through to the " +
                "generic `default:` branch (BP4004 warning), no IR emitted. Doc comment says it " +
                "'wraps ElementPartitionPrimitive.Partition' but that wiring was never implemented.",
            [typeof(AssignRolesNode)] =
                "No Stage5_Schedule case for AssignRolesNode -- falls through to the generic " +
                "`default:` branch (BP4004 warning), no IR emitted. Doc comment says it 'wraps " +
                "RoleSlotAssignmentPrimitive.AssignRoles' but that wiring was never implemented.",
            [typeof(AdvancePhaseNode)] =
                "No Stage5_Schedule case for AdvancePhaseNode -- falls through to the generic " +
                "`default:` branch (BP4004 warning), no IR emitted. Doc comment says it 'wraps " +
                "PhaseSequencer.Advance' but that wiring was never implemented.",
            [typeof(AcquireSlotNode)] =
                "No Stage5_Schedule case for AcquireSlotNode -- falls through to the generic " +
                "`default:` branch (BP4004 warning), no IR emitted. Doc comment says it 'wraps " +
                "SlotRotation.AcquireSlot' but that wiring was never implemented.",
            [typeof(ArrayMakeNode)] =
                "No Stage5_Schedule case for ArrayMakeNode as an exec statement -- falls through " +
                "to the generic `default:` branch (BP4004 warning), no IR emitted for the exec " +
                "chain. Its 'Array' output pin is also unhandled by the separate pure-data-value " +
                "resolver (ResolveNodeOutput), which silently returns a dummy default(...) value " +
                "for any unrecognized source node -- i.e. reading ArrayMake's output produces a " +
                "silent wrong-value bug with no diagnostic at all, worse than the BP4004 case.",
            [typeof(ArrayGetNode)] =
                "No Stage5_Schedule case for ArrayGetNode as an exec statement -- falls through " +
                "to the generic `default:` branch (BP4004 warning), no IR emitted for the exec " +
                "chain. Its 'Element' output pin is also unhandled by the separate pure-data-value " +
                "resolver (ResolveNodeOutput), which silently returns a dummy default(...) value " +
                "for any unrecognized source node -- i.e. reading ArrayGet's output produces a " +
                "silent wrong-value bug with no diagnostic at all, worse than the BP4004 case.",
            [typeof(CallEventDispatcherNode)] =
                "No Stage5_Schedule case for CallEventDispatcherNode -- falls through to the " +
                "generic `default:` branch (BP4004 warning), no IR emitted.",
            [typeof(BindEventDispatcherNode)] =
                "No Stage5_Schedule case for BindEventDispatcherNode -- falls through to the " +
                "generic `default:` branch (BP4004 warning), no IR emitted.",
            [typeof(WaitForEventNode)] =
                "DIFFERENT bug from the eight kinds above (no BP4004 involved -- this one has a " +
                "real Stage5_Schedule case). Stage2_Validate's V_WaitNodeReferences (BP1402) only " +
                "accepts a SHORT event-type name (matches BuiltInWaitPrimitiveCatalog entries by " +
                "last-segment), but Stage5_Schedule.BuildWaitForEventOp passes that same short " +
                "EventTypeId straight through as the emitted C# type reference with NO catalog FQN " +
                "resolution -- unlike WaitForChannelNode's parallel BuildWaitForChannelOp, which " +
                "DOES resolve its short ChannelType to a FQN via ResolveChannelTypeFqn before " +
                "emitting. Net effect: a WaitForEventNode graph that PASSES Stage2 validation " +
                "(short name) always FAILS the Roslyn compile with CS0400 'type could not be " +
                "found' (bare short name used as a type reference); a graph with the FULL FQN " +
                "compiles through Roslyn but FAILS Stage2 validation (BP1402) first. There is no " +
                "EventTypeId value that satisfies both stages today -- a real, structural compiler " +
                "bug. See WaitForEventNode_ShortEventTypeId_ValidatesButFailsRoslynCompile_BUG " +
                "(a documenting Skip'd test) below.",
        };

    /// <summary>
    /// The exception set itself must only ever name real concrete node kinds with a non-empty
    /// documented reason -- keeps the exception list reviewable (task requirement: "have the
    /// guard assert the exceptions set only contains those documented kinds").
    /// </summary>
    [Fact]
    public void CompileCoverageExceptions_OnlyContainsDocumentedConcreteNodeKinds()
    {
        var allKinds = AllConcreteNodeKinds();
        foreach (var (type, reason) in CompileCoverageExceptions)
        {
            Assert.Contains(type, allKinds);
            Assert.False(string.IsNullOrWhiteSpace(reason),
                $"{type.Name}: exception reason must not be empty.");
        }
    }

    // WaitForEventNode's exception has a DIFFERENT root cause (no BP4004 involved -- see its
    // reason string above) and is characterized separately by
    // WaitForEventNode_ShortEventTypeId_ValidatesButFailsRoslynCompile_BUG below.
    public static IEnumerable<object[]> CompileCoverageExceptionTheoryData() =>
        CompileCoverageExceptions.Keys
            .Where(t => t != typeof(WaitForEventNode))
            .Select(t => new object[] { t });

    /// <summary>
    /// Characterizes the CURRENT (buggy) behavior of every BP4004-excepted node kind: it
    /// compiles "successfully" only because Stage5_Schedule's default case swallows it with a
    /// BP4004 *warning* and emits no IR. If this test starts failing for a kind -- i.e. BP4004 is
    /// no longer emitted, because real lowering was implemented -- that kind must be REMOVED from
    /// <see cref="CompileCoverageExceptions"/> and given real fixture coverage in
    /// <see cref="CoverageAssets"/> instead of staying excepted.
    /// </summary>
    [Theory]
    [MemberData(nameof(CompileCoverageExceptionTheoryData))]
    public void ExceptedNodes_CurrentlyHitUnimplementedLoweringWarning(Type nodeType)
    {
        var asset = BuildSingleExecNodeAsset(nodeType);
        var options = DefaultCompileOptions();

        var result = new BlueprintCompiler().Compile(asset, options);

        Assert.True(result.Succeeded,
            $"{nodeType.Name}: expected the compile to 'succeed' (Stage5_Schedule only warns via BP4004).");
        Assert.Contains(result.Diagnostics, d =>
            d.Code == DiagnosticCodes.BP4004 && d.Severity == DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// BUG (documented, not papered over): a WaitForEventNode whose EventTypeId is the SHORT
    /// name Stage2_Validate's V_WaitNodeReferences (BP1402) requires (matched against
    /// BuiltInWaitPrimitiveCatalog by last-segment) FAILS the real Roslyn compile with CS0400
    /// ("type could not be found"), because Stage5_Schedule.BuildWaitForEventOp
    /// (Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs) emits that short name
    /// straight through as the C# type reference with no catalog FQN resolution -- unlike
    /// WaitForChannelNode's parallel BuildWaitForChannelOp, which resolves its short ChannelType
    /// to a FQN via ResolveChannelTypeFqn first. There is no EventTypeId value that satisfies
    /// both Stage2 validation and Roslyn compilation today.
    /// <para>
    /// This test asserts the DESIRED behavior (a validation-passing WaitForEventNode graph
    /// actually compiles+loads) and is Skip'd because that is currently false. Un-skip it once
    /// BuildWaitForEventOp gains a resolution step mirroring ResolveChannelTypeFqn; then also
    /// remove WaitForEventNode from CompileCoverageExceptions and move its fixture into
    /// CoverageAssets as real FullRoslynPipeline coverage.
    /// </para>
    /// </summary>
    [Fact(Skip = "BUG: WaitForEventNode's short EventTypeId passes BP1402 validation but is never " +
                 "resolved to a FQN before emit, so Roslyn always fails with CS0400. See doc comment.")]
    public void WaitForEventNode_ShortEventTypeId_ValidatesButFailsRoslynCompile_BUG()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // "BehaviorFinishedEvent" is the short name BP1402 requires (matches
        // BuiltInWaitPrimitiveCatalog's "Fdp.Toolkit.Behavior.Events.BehaviorFinishedEvent" entry
        // by last segment). Today this throws BlueprintCompileException (CS0400) inside
        // CompileAndLoad; once fixed, it should just return a loaded Assembly.
        var asset = BuildWaitForEventMinimalAsset();
        var assembly = fixture.CompileAndLoad(asset);

        Assert.NotNull(assembly);
    }

    /// <summary>
    /// Every concrete node kind must be covered by a compiling fixture (evidence: it appears in
    /// a Blueprint asset that actually compiles+loads through <see cref="BlueprintTestFixture"/>'s
    /// real Roslyn+ALC pipeline in <see cref="CoverageAssets"/>) OR be listed in
    /// <see cref="CompileCoverageExceptions"/>. A newly-added node kind with neither fails this
    /// test immediately -- that is the self-enforcing property this guard exists for.
    /// </summary>
    [Fact]
    public void AllNodeKinds_AreCoveredByACompilingFixtureOrADocumentedException()
    {
        var coveredKinds = new HashSet<Type>();

        foreach (var (description, assets, options, mode) in CoverageAssets())
        {
            switch (mode)
            {
                case CoverageMode.FullRoslynPipeline:
                {
                    // Real Roslyn compile + collectible-ALC load through BlueprintTestFixture --
                    // the strongest evidence: proves the generated C# actually compiles and loads.
                    using var fixture = new BlueprintTestFixture(
                        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

                    Assembly assembly;
                    try
                    {
                        assembly = options is null
                            ? fixture.CompileAndLoadMany(assets)
                            : fixture.CompileAndLoadMany(assets, options);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"Coverage fixture '{description}' failed to compile+load: {ex}");
                        return;
                    }
                    Assert.NotNull(assembly);
                    break;
                }

                case CoverageMode.ValidateOnlyStage1To7:
                {
                    // Weaker evidence, used ONLY where a real reason blocks the full Roslyn path
                    // (see BuildCallPeerBlueprintMinimalAsset's doc comment) -- runs the asset
                    // through the real multi-stage BlueprintCompiler (Stage1_Parse..Stage7_Emit,
                    // i.e. everything except Roslyn) and requires zero Error diagnostics. This is
                    // the SAME evidence bar RecipeIntegrityTests.SquadAwareEngagement_ValidateOnly_NoErrors
                    // already uses in this codebase for the identical cross-asset-peer reason.
                    var result = new BlueprintCompiler().Compile(assets[0], options ?? DefaultCompileOptions());
                    var errors = result.Diagnostics.Where(d => d.IsError).ToList();
                    if (errors.Count > 0)
                    {
                        Assert.Fail($"Coverage fixture '{description}' failed Stage1-7 validation: " +
                                    string.Join(", ", errors.Select(e => $"{e.Code}: {e.Message}")));
                        return;
                    }
                    break;
                }
            }

            foreach (var kind in assets.SelectMany(a => a.Graphs).SelectMany(g => g.Nodes).Select(n => n.GetType()).Distinct())
                coveredKinds.Add(kind);
        }

        var missing = AllConcreteNodeKinds()
            .Where(k => !coveredKinds.Contains(k) && !CompileCoverageExceptions.ContainsKey(k))
            .Select(k => k.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "Node kind(s) with no compiling fixture in CoverageAssets() and no documented " +
            "exception in CompileCoverageExceptions: " + string.Join(", ", missing) +
            ". Add a compiling fixture, or a documented exception with a real reason.");
    }

    // ========================================================================
    // FlowForEach loop-introspection outs (CurrentIndex + Count) -- emitted-C# proof
    // ========================================================================

    /// <summary>
    /// Headless proof for the FlowForEach <c>CurrentIndex</c> + <c>Count</c> data-out pins: compiles
    /// <see cref="BuildFlowForEachIndexCountMinimalAsset"/> through the real Stage1-7 pipeline and
    /// asserts the generated C# has (a) the element count HOISTED into an outer-scope local
    /// (<c>var __t.. = global::…UnitRosterOps.Count(__t..);</c>) rather than re-evaluated in the loop
    /// bound, (b) the loop counter COPIED into a body-scope local
    /// (<c>var __t.. = __fe..;</c>), and (c) both consumed by the body's arithmetic BinaryOp
    /// (<c>__t.. - __t..</c>). This locks the emission contract without needing the game assemblies a
    /// full Roslyn compile would (see the fixture's ValidateOnlyStage1To7 registration).
    /// </summary>
    [Fact]
    public void FlowForEach_IndexAndCount_EmitsHoistedCountAndBodyIndexCopy()
    {
        var result = new BlueprintCompiler()
            .Compile(BuildFlowForEachIndexCountMinimalAsset(), DefaultCompileOptions());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        Assert.NotNull(src);

        // (a) Count hoisted to an OUTER-scope local: the accessor appears as an assignment RHS
        // (`= global::…Count(`), which the non-hoisted path never emits (there it is only ever a loop
        // bound `< global::…Count(`).
        Assert.Contains("= global::Hrot.AI.Behaviors.Brains.UnitRosterOps.Count(", src);
        // ...and the for-loop bound is that hoisted local, not a fresh Count() re-eval each pass.
        Assert.Matches(@"for \(int __fe\d+ = 0; __fe\d+ < __t\d+;", src);
        Assert.DoesNotMatch(@"< global::Hrot\.AI\.Behaviors\.Brains\.UnitRosterOps\.Count\(", src);

        // (b) Loop counter copied into a body-scope local for CurrentIndex.
        Assert.Matches(@"var __t\d+ = __fe\d+;", src);

        // (c) Both loop outs consumed by the body's arithmetic BinaryOp (Subtract -> infix ` - `).
        Assert.Matches(@"__t\d+ - __t\d+", src);
    }

    // ========================================================================
    // Guard 4 -- pin-less round-trip: Stage0 must reproduce author-placed DATA pins
    // ========================================================================

    /// <summary>
    /// Node kinds whose author-placed data pins are legitimately NOT reproduced by Stage0's pin-less
    /// rehydration in this harness — each with a documented, reviewed reason. Anything not listed here
    /// MUST round-trip, so a new kind with a rehydration gap fails <see cref="AllCoverageNodes_PinlessRehydration_ReproducesAuthoredDataPins"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> PinlessRoundTripExceptions =
        new Dictionary<Type, string>
        {
            [typeof(CallPeerBlueprintNode)] =
                "Peer data pins come from a sibling BlueprintSignature resolved via the generator's " +
                "cross-asset pass; this isolated Stage0 run has no sibling lookup wired, so it yields the " +
                "exec-only + Return fallback. Real coverage is CoverageAssets' Inline/CallPeerBlueprint " +
                "(ValidateOnlyStage1To7) which DOES pass the sibling signature.",
            [typeof(FunctionCallNode)] =
                "FunctionCall data pins are SIGNATURE-derived (real param names): the editor reflects " +
                "and the generator uses the Roslyn semantic model (RoslynClrSignatureResolver). These " +
                "coverage fixtures author placeholder pin names (A/B/Result, Value) that do not match " +
                "any real method signature, so Stage0's reflection/resolver legitimately produces " +
                "different names. The real pin-less FunctionCall round-trip (real param names) is proven " +
                "by FunctionCallSemanticResolveTests + the pin-less HillAssault2I_* proof blueprints.",
            [typeof(GetParameterNode)] =
                "GetParameter's 'Value' output is resolved at lowering from the node's BAKED ParameterId " +
                "(like GetVariable), not by pin lookup, so it round-trips pin-less without the output pin " +
                "being reconstructed. Proven by the stripped HillAssault2I_IsWaveCompleted blueprint " +
                "(which reads a parameter) building green.",
        };

    /// <summary>
    /// Guard 4 — pin-less round-trip. Every DATA pin an author/editor places on a node must be
    /// reproduced by <see cref="Stage0_Rehydrate"/> when the node is stored pin-less (<c>"Pins": []</c>,
    /// the editor-save form). This is the exact invariant that broke for FunctionCall / PublishEvent /
    /// ChannelCommand / Compare: a kind whose data pins come from a catalog/signature but whose pin-less
    /// rehydration produced fewer pins, so a migrated (editor-saved OR AI-authored pin-less) blueprint
    /// lost wires (BP1602) or dropped producers (CS0103). Reusing every <see cref="CoverageAssets"/>
    /// fixture makes it self-enforcing: because Guard 3 forces every concrete node kind to have a
    /// compiling fixture, a newly-added kind with a data-pin rehydration gap fails HERE too.
    /// <para>Only DATA pins (non-exec) are compared, by (name, direction) — exec pins are always present
    /// and their names are conventional; the data pins are where the rehydration gap risk lives.</para>
    /// </summary>
    [Fact]
    public void AllCoverageNodes_PinlessRehydration_ReproducesAuthoredDataPins()
    {
        var failures = new List<string>();

        foreach (var (description, assets, options, _) in CoverageAssets())
        {
            var opts = options ?? DefaultCompileOptions();
            foreach (var asset in assets)
            {
                // Snapshot the author-placed DATA-pin shape per node id (before stripping).
                var authored = asset.Graphs.SelectMany(g => g.Nodes).ToDictionary(
                    n => n.Id,
                    n => n.Pins.Where(p => !p.IsExec).Select(p => (p.Name, p.Direction)).ToHashSet());

                // Editor-save form: same graph, every node pin-less. Deep-clone via JSON so the original
                // (shared across CoverageAssets iterations) is untouched.
                var clone = BlueprintJsonServices.Deserialize(BlueprintJsonServices.Serialize(asset))!;
                foreach (var n in clone.Graphs.SelectMany(g => g.Nodes))
                    n.Pins = new List<Pin>();

                Stage0_Rehydrate.Run(clone, opts);

                foreach (var n in clone.Graphs.SelectMany(g => g.Nodes))
                {
                    if (PinlessRoundTripExceptions.ContainsKey(n.GetType())) continue;
                    if (!authored.TryGetValue(n.Id, out var want) || want.Count == 0) continue;

                    var got = n.Pins.Where(p => !p.IsExec).Select(p => (p.Name, p.Direction)).ToHashSet();
                    var missing = want.Except(got).ToList();
                    if (missing.Count > 0)
                        failures.Add($"{description} / {n.GetType().Name}: pin-less rehydration is missing " +
                            "author-placed data pin(s): " +
                            string.Join(", ", missing.Select(m => $"{m.Name}:{m.Direction}")));
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Pin-less rehydration dropped author-placed data pins — a round-trip gap. Add a Stage0 " +
            "enricher (mirror EnrichPublishEventPins/EnrichChannelCommandPins) or complete static " +
            "registry pins for the kind, or document a PinlessRoundTripExceptions reason:\n" +
            string.Join("\n", failures));
    }

    private enum CoverageMode { FullRoslynPipeline, ValidateOnlyStage1To7 }

    // ── coverage asset providers ───────────────────────────────────────────

    private static CompileOptions DefaultCompileOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>
    /// Yields (description, assets, options, mode) tuples used as compile-coverage evidence.
    /// Each entry's <c>assets</c> are compiled TOGETHER in one merged compilation (a
    /// single-asset entry is just a one-element list). `options: null` means use
    /// <see cref="BlueprintTestFixture"/>'s own default CompileOptions
    /// (<c>BuiltInChannelCommandCatalog</c> etc.). <c>mode</c> is
    /// <see cref="CoverageMode.FullRoslynPipeline"/> for everything except the CallPeerBlueprint
    /// fixture -- see <see cref="BuildCallPeerBlueprintMinimalAsset"/>'s doc comment for why that
    /// one uses <see cref="CoverageMode.ValidateOnlyStage1To7"/> instead. Prefers reusing
    /// existing demo/recipe/test assets over inventing new ones; only the kinds with genuinely
    /// no existing coverage (CallPeerBlueprint, Cast, CallCustomEvent, WaitForEvent,
    /// ScoreDecision, ReadRankedResult, GetShared, SetShared) get a purpose-built minimal
    /// fixture below.
    /// </summary>
    private static IEnumerable<(string Description, IReadOnlyList<BlueprintAsset> Assets, CompileOptions? Options, CoverageMode Mode)> CoverageAssets()
    {
        // ---- Existing recipe assets (already relied on elsewhere as valid, compiling content
        //      via RecipeIntegrityTests.AllRecipes_ValidateOnly_NoErrors / *_ValidateOnly_NoErrors).
        //      Covers: EventEntry, ChannelCommand, When, ReadEqsResult, SpawnEqsSensor,
        //      WaitForChannel, GetVariable, Branch, FunctionCall, Return, AdvancePhase, AssignRoles.
        foreach (var name in new[]
                 {
                     "CoverAwarePatrol", "HealthThresholdReaction", "MoveAndFireCombo",
                     "SquadState", "EditorTypesDemo", "GateConditionDemo", "LocomotionMoveToDemo",
                 })
        {
            yield return ($"Recipes/{name}", new[] { LoadRecipe(name) }, null, CoverageMode.FullRoslynPipeline);
        }

        // BoundingOverwatchSwap: production worked example for AdvancePhase/AssignRoles (already
        // asserted to contain them by SquadPrimitiveNodeTests.BoundingOverwatchSwap_Blueprint_LoadsAndContainsSquadNodes).
        // Not used for compile-coverage credit here since those two kinds are excepted (BP4004) --
        // see CompileCoverageExceptions -- but harmless/representative to include for the record.

        // SquadAwareEngagement needs SquadState as a sibling to pass BP2004 peer-reference validation.
        {
            var squadState = LoadRecipe("SquadState");
            var siblingOptions = DefaultCompileOptions() with
            {
                SiblingSignatures = new[]
                {
                    new BlueprintSignature(
                        Path:              "",
                        AssetId:           squadState.AssetId,
                        Name:              squadState.Name,
                        SanitizedName:     squadState.Name,
                        BlueprintId:       0,
                        Dispatch:          squadState.Dispatch,
                        ExportedFunctions: squadState.Graphs
                            .Where(g => g.Kind == GraphKind.Function)
                            .Select(g => new BlueprintFunctionSig(
                                g.Name,
                                g.Inputs.Select(p => new BlueprintParamSig(
                                    p.Name, string.IsNullOrEmpty(p.Type?.TypeId) ? "System.Object" : p.Type.TypeId)).ToArray(),
                                g.Outputs.Select(p => new BlueprintParamSig(
                                    p.Name, string.IsNullOrEmpty(p.Type?.TypeId) ? "System.Object" : p.Type.TypeId)).ToArray()))
                            .ToArray(),
                        Hostings:              Array.Empty<AiPrimitiveHosting>(),
                        DeclaredCallablePeers: squadState.CallablePeers.ToArray()),
                },
            };
            yield return ("Recipes/SquadAwareEngagement", new[] { LoadRecipe("SquadAwareEngagement") }, siblingOptions, CoverageMode.FullRoslynPipeline);
        }

        // ---- Existing TestAssets samples already relied on by other end-to-end suites.
        //      Covers: FunctionCall, GetVariable, Literal, Return, SetVariable, Delay, Sequence.
        foreach (var name in new[] { "CountingDemo", "Count4" })
            yield return (name, new[] { TestData.LoadAsset(name) }, null, CoverageMode.FullRoslynPipeline);

        // ---- Minimal purpose-built fixtures for kinds with genuinely no existing coverage.
        // (with-callable-peer.bp.json in TestAssets/ has an unset PeerBlueprintId -- it exists
        // only for a structural/round-trip check elsewhere, not a real compile -- so
        // CallPeerBlueprint gets its own minimal fixture here instead of reusing that file.)
        {
            var (callerAsset, callerOptions) = BuildCallPeerBlueprintMinimalAsset();
            yield return ("Inline/CallPeerBlueprint", new[] { callerAsset }, callerOptions, CoverageMode.ValidateOnlyStage1To7);
        }
        yield return ("Inline/Cast", new[] { BuildCastMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/CallCustomEvent", new[] { BuildCallCustomEventMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        // WaitForEventNode is NOT listed here -- see CompileCoverageExceptions[typeof(WaitForEventNode)]
        // and WaitForEventNode_ShortEventTypeId_ValidatesButFailsRoslynCompile_BUG: no EventTypeId
        // value passes both Stage2 validation AND Roslyn compilation today.
        yield return ("Inline/ScoreDecision", new[] { BuildScoreDecisionMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/ReadRankedResult", new[] { BuildReadRankedResultMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/GetSharedSetShared", new[] { BuildGetSetSharedMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/GetComponent", new[] { BuildGetComponentMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/SetComponent", new[] { BuildSetComponentMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/DiamondMerge", new[] { BuildDiamondMergeMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/GetParameter", new[] { BuildGetParameterMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/GetAllParameters", new[] { BuildGetAllParametersMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/Compare", new[] { BuildCompareMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/BinaryOp", new[] { BuildBinaryOpMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/BooleanOp", new[] { BuildBooleanOpMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        yield return ("Inline/Not", new[] { BuildNotMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
        // PublishEvent (P4 -- GAP-3): ValidateOnlyStage1To7 (not FullRoslynPipeline) because the
        // generated C# references Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent AND
        // EntityRepository.Bus, neither of which the deliberately dependency-light coverage-Roslyn
        // compilation references (see BuildGetComponentMinimalAsset, which used System.Numerics.Vector3
        // precisely to avoid game-assembly deps). Stage1-7 fully exercises the PublishEventNode ->
        // IrOp_PublishBusEvent lowering (catalog lookup + self-default Target); the REAL Roslyn
        // compile of a PublishEvent graph is proven separately by HillAssault2_ClearBehavior_ProofTests
        // through the actual Hrot.AI.Behaviors build. Same evidence bar + reason as CallPeerBlueprint.
        yield return ("Inline/PublishEvent", new[] { BuildPublishEventMinimalAsset() }, null, CoverageMode.ValidateOnlyStage1To7);
        // FlowForEach (P1 -- GAP-1): ValidateOnlyStage1To7 -- the generated for-loop references
        // UnitRosterOps/UnitRoster (game assemblies the coverage-Roslyn compile does not reference);
        // the REAL Roslyn compile of a FlowForEach graph is proven by
        // HillAssault2_ForEachSubordinate_ProofTests through the actual Hrot.AI.Behaviors build.
        yield return ("Inline/FlowForEach", new[] { BuildFlowForEachMinimalAsset() }, null, CoverageMode.ValidateOnlyStage1To7);
        // FlowForEach loop-introspection outs (CurrentIndex + Count): same game-assembly reason as
        // above -> ValidateOnlyStage1To7. The GENERATED C# (count-hoist local + body index-copy +
        // BinaryOp consuming both) is asserted verbatim by
        // FlowForEach_IndexAndCount_EmitsHoistedCountAndBodyIndexCopy below; a real Roslyn build lands
        // with the DispatchAllToBaseline slice which uses these outs.
        yield return ("Inline/FlowForEachIndexCount", new[] { BuildFlowForEachIndexCountMinimalAsset() }, null, CoverageMode.ValidateOnlyStage1To7);
        // Component-collection consumers (CA-07b): same game-assembly reason as FlowForEach ->
        // ValidateOnlyStage1To7 (BpCollectionDemoOps lives in Hrot.AI.Behaviors). The generated C#
        // (re-read off the resolved entity + accessor calls + the ForEach loop shape) is asserted
        // verbatim by ComponentCollectionConsumerLoweringTests.
        yield return ("Inline/ComponentItemCount", new[] { BuildComponentItemCountMinimalAsset() }, null, CoverageMode.ValidateOnlyStage1To7);
        yield return ("Inline/ComponentItemGet", new[] { BuildComponentItemGetMinimalAsset() }, null, CoverageMode.ValidateOnlyStage1To7);
        yield return ("Inline/ComponentForEach", new[] { BuildComponentForEachMinimalAsset() }, null, CoverageMode.ValidateOnlyStage1To7);
    }

    // ---- recipe loading (mirrors RecipeIntegrityTests.LoadRecipe) -----------

    private static BlueprintAsset LoadRecipe(string name)
    {
        var aiBehaviorsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");

        string path;
        if (aiBehaviorsAssembly != null)
        {
            var assemblyLocation = Path.GetDirectoryName(aiBehaviorsAssembly.Location)
                ?? throw new InvalidOperationException("Cannot determine Hrot.AI.Behaviors assembly location.");
            path = Path.Combine(assemblyLocation, "Recipes", "Blueprints", name + ".bp.json");
        }
        else
        {
            var dir = TestData.ResolveTestAssetsDir();
            path = Path.Combine(dir, "Recipes", name + ".bp.json");
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"Recipe file not found: {path}");

        var json = File.ReadAllText(path);
        return BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException($"Deserialized null from '{path}'");
    }

    // ---- minimal inline fixtures --------------------------------------------

    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>
    /// Builds EventEntry -&gt; <paramref name="nodeType"/> -&gt; Return, wiring only plain
    /// ExecIn/ExecOut pins on the middle node (sufficient to place it in the exec chain; used
    /// only by <see cref="ExceptedNodes_CurrentlyHitUnimplementedLoweringWarning"/>, which does
    /// not need the node's real data pins since it only asserts BP4004 is emitted).
    /// </summary>
    private static BlueprintAsset BuildSingleExecNodeAsset(Type nodeType)
    {
        var node = (Node)Activator.CreateInstance(nodeType)!;
        node.Id = Guid.NewGuid();
        var nodeIn  = ExecPin("In",  "In");
        var nodeOut = ExecPin("Out", "Out");
        node.Pins.Add(nodeIn);
        node.Pins.Add(nodeOut);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, node, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = node.Id, ToPinId = nodeIn.Id },
                new Link { FromNodeId = node.Id,  FromPinId = nodeOut.Id,  ToNodeId = ret.Id,  ToPinId = retIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "ExceptionProbe_" + nodeType.Name,
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// Builds a caller Instance asset (EventEntry -&gt; CallPeerBlueprint("Ping") -&gt; Return) with a
    /// sibling <see cref="BlueprintSignature"/> for a peer that is never itself compiled here.
    /// <para>
    /// Uses <see cref="CoverageMode.ValidateOnlyStage1To7"/> (BlueprintCompiler.Compile, no
    /// Roslyn) rather than the full Roslyn+ALC path: the generated C# for a peer call is
    /// <c>__Peer_{hash:X8}_Bp.MethodName(...)</c> (see StatementEmitter.cs's IrOp_PeerCall case)
    /// -- a bare class name with NO <c>using</c> alias or forwarding stub emitted anywhere in this
    /// compiler. In production that class only exists because Hrot.AI.Behaviors' build compiles
    /// ALL sibling blueprints together via its own source-generator pass; the isolated
    /// BlueprintTestFixture harness has no equivalent step, so a real Roslyn compile of a lone
    /// caller (or caller+peer merged by hand) cannot resolve that symbol. This is the SAME
    /// structural reason RecipeIntegrityTests.SquadAwareEngagement_ValidateOnly_NoErrors already
    /// stops at Stage1-7 for cross-asset peer references in this codebase -- not a shortcut
    /// invented for this guard.
    /// </para>
    /// </summary>
    private static (BlueprintAsset Caller, CompileOptions Options) BuildCallPeerBlueprintMinimalAsset()
    {
        var peerAssetId = Guid.NewGuid();

        var peerSignature = new BlueprintSignature(
            Path:              "",
            AssetId:           peerAssetId,
            Name:              "PeerLib",
            SanitizedName:     "PeerLib",
            BlueprintId:       0,
            Dispatch:          BlueprintDispatchKind.Library,
            ExportedFunctions: new[] { new BlueprintFunctionSig("Ping", Array.Empty<BlueprintParamSig>(), Array.Empty<BlueprintParamSig>()) },
            Hostings:              Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: Array.Empty<Guid>());

        var callNode = new CallPeerBlueprintNode
        {
            Id              = Guid.NewGuid(),
            PeerBlueprintId = peerAssetId.ToString(),
            FunctionRef     = "Ping",
        };
        var callIn  = ExecPin("In",  "In");
        var callOut = ExecPin("Out", "Out");
        callNode.Pins.AddRange(new[] { callIn, callOut });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, callNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id, ToNodeId = callNode.Id, ToPinId = callIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = callOut.Id,  ToNodeId = ret.Id,       ToPinId = retIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId       = Guid.NewGuid(),
            Name          = "CallPeerBlueprintCoverage",
            Dispatch      = BlueprintDispatchKind.Instance,
            CallablePeers = { peerAssetId },
            Graphs        = { graph },
        };

        var options = DefaultCompileOptions() with { SiblingSignatures = new[] { peerSignature } };
        return (asset, options);
    }

    /// <summary>EventEntry -&gt; Cast(TargetTypeId=System.Object) -&gt; Return, fed by a Literal.</summary>
    private static BlueprintAsset BuildCastMinimalAsset()
    {
        var literal   = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Object", ValueJson = "null" };
        var literalOut = DataPin("Value", "Out", "System.Object");
        literal.Pins.Add(literalOut);

        var cast     = new CastNode { Id = Guid.NewGuid(), TargetTypeId = "System.Object" };
        var castExecIn  = ExecPin("In",  "In");
        var castExecOut = ExecPin("Out", "Out");
        var castDataIn  = DataPin("In",  "In",  "System.Object");
        var castDataOut = DataPin("Out", "Out", "System.Object");
        cast.Pins.AddRange(new[] { castExecIn, castExecOut, castDataIn, castDataOut });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, literal, cast, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id,  ToNodeId = cast.Id, ToPinId = castExecIn.Id },
                new Link { FromNodeId = cast.Id,  FromPinId = castExecOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id },
                new Link { FromNodeId = literal.Id, FromPinId = literalOut.Id, ToNodeId = cast.Id, ToPinId = castDataIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "CastCoverage",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; CallCustomEvent("Ping") -&gt; Return, with a matching CustomEvents decl AND
    /// a "Ping" Event-kind graph (mirrors with-custom-event.bp.json's EventEntry-only event
    /// graph). The generated raise-event statement calls a fixed-name method
    /// <c>Event_{CustomEventName}(...)</c> (see StatementEmitter.cs's IrOp_RaiseCustomEvent case)
    /// that only exists because InstanceEmitter emits one such method per Event-kind graph named
    /// after the custom event -- so, unlike most other node kinds, this fixture needs a SECOND
    /// graph, not just the calling one.
    /// </summary>
    private static BlueprintAsset BuildCallCustomEventMinimalAsset()
    {
        var callEvent = new CallCustomEventNode { Id = Guid.NewGuid(), EventId = "Ping" };
        var callIn  = ExecPin("In",  "In");
        var callOut = ExecPin("Out", "Out");
        callEvent.Pins.AddRange(new[] { callIn, callOut });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var mainGraph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, callEvent, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryOut.Id, ToNodeId = callEvent.Id, ToPinId = callIn.Id },
                new Link { FromNodeId = callEvent.Id, FromPinId = callOut.Id,  ToNodeId = ret.Id,        ToPinId = retIn.Id },
            },
        };

        var pingEntry    = new EventEntryNode { Id = Guid.NewGuid() };
        var pingEntryOut = ExecPin("ExecOut", "Out");
        pingEntry.Pins.Add(pingEntryOut);
        var pingGraph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Ping",
            Kind  = GraphKind.Event,
            Nodes = { pingEntry },
        };

        return new BlueprintAsset
        {
            AssetId      = Guid.NewGuid(),
            Name         = "CallCustomEventCoverage",
            Dispatch     = BlueprintDispatchKind.Instance,
            CustomEvents = { new CustomEventDecl { Id = Guid.NewGuid(), Name = "Ping" } },
            Graphs       = { mainGraph, pingGraph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; WaitForEvent -&gt; Return. EventTypeId is the SHORT name required to pass
    /// Stage2_Validate's V_WaitNodeReferences (BP1402) -- last-segment match against
    /// <see cref="BuiltInWaitPrimitiveCatalog"/>. Used ONLY by
    /// <see cref="WaitForEventNode_ShortEventTypeId_ValidatesButFailsRoslynCompile_BUG"/> to
    /// characterize the documented bug (this exact asset FAILS Roslyn compilation -- see
    /// CompileCoverageExceptions[typeof(WaitForEventNode)]), not used as real coverage evidence.
    /// </summary>
    private static BlueprintAsset BuildWaitForEventMinimalAsset()
    {
        var wait = new WaitForEventNode { Id = Guid.NewGuid(), EventTypeId = "BehaviorFinishedEvent" };
        var waitIn  = ExecPin("In",  "In");
        var waitOut = ExecPin("Out", "Out");
        wait.Pins.AddRange(new[] { waitIn, waitOut });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, wait, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = wait.Id, ToPinId = waitIn.Id },
                new Link { FromNodeId = wait.Id,  FromPinId = waitOut.Id,  ToNodeId = ret.Id,  ToPinId = retIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "WaitForEventCoverage",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; ScoreDecision -&gt; SetVariable(PostureOut) -&gt; Return. Mirrors
    /// UtilityNodeRuntimeTests.ScoreDecisionNode_Produces_WinningOption's shape, trimmed to
    /// what compiling (not ticking) requires -- no UtilityDecisionCatalog registration needed,
    /// since the generated helper only bakes the decision id as a literal at compile time.
    /// </summary>
    private static BlueprintAsset BuildScoreDecisionMinimalAsset()
    {
        var postureVarId = Guid.NewGuid();
        var postureVar = new VariableDecl
        {
            Id   = postureVarId,
            Name = "PostureOut",
            Type = new BlueprintTypeRef { TypeId = "System.Byte" },
        };

        var scoreExecIn  = ExecPin("ExecIn",  "In");
        var scoreExecOut = ExecPin("ExecOut", "Out");
        var winningPin   = DataPin("WinningOptionId", "Out", "System.Byte");
        var scoreNode    = new ScoreDecisionNode { Id = Guid.NewGuid(), AssetId = "3c6f9e42-5d10-6f3a-ac23-posture0000001" };
        scoreNode.Pins.AddRange(new[] { scoreExecIn, scoreExecOut, winningPin });

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setDataIn  = DataPin("Value", "In", "System.Byte");
        var setNode    = new SetVariableNode { Id = Guid.NewGuid(), VariableId = postureVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, scoreNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryOut.Id,      ToNodeId = scoreNode.Id, ToPinId = scoreExecIn.Id },
                new Link { FromNodeId = scoreNode.Id, FromPinId = scoreExecOut.Id,  ToNodeId = setNode.Id,   ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,   FromPinId = setExecOut.Id,    ToNodeId = ret.Id,       ToPinId = retIn.Id },
                new Link { FromNodeId = scoreNode.Id, FromPinId = winningPin.Id,    ToNodeId = setNode.Id,   ToPinId = setDataIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ScoreDecisionCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { postureVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(TopEntity) -&gt; Return, fed by a data-only ReadRankedResult
    /// node. Mirrors UtilityNodeRuntimeTests.ReadRankedResultNode_Reads_TopBufferEntry's shape,
    /// trimmed to what compiling (not ticking) requires.
    /// </summary>
    private static BlueprintAsset BuildReadRankedResultMinimalAsset()
    {
        var topEntityVarId = Guid.NewGuid();
        var topEntityVar = new VariableDecl
        {
            Id   = topEntityVarId,
            Name = "TopEntity",
            Type = new BlueprintTypeRef { TypeId = "System.Int64" },
        };

        var entityOutPin = DataPin("Entity",  "Out", "System.Int64");
        var scoreOutPin  = DataPin("Score",   "Out", "System.Single");
        var isValidOutPin = DataPin("IsValid", "Out", "System.Boolean");
        var readNode = new ReadRankedResultNode { Id = Guid.NewGuid(), Rank = 0 };
        readNode.Pins.AddRange(new[] { entityOutPin, scoreOutPin, isValidOutPin });

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setDataIn  = DataPin("Value", "In", "System.Int64");
        var setNode    = new SetVariableNode { Id = Guid.NewGuid(), VariableId = topEntityVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, readNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,   ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = readNode.Id, FromPinId = entityOutPin.Id, ToNodeId = setNode.Id, ToPinId = setDataIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ReadRankedResultCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { topEntityVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetShared("x", System.Int32) -&gt; Return, fed by GetShared("x",
    /// System.Int32). Uses <c>System.Int32</c> as the shared-struct type (proven to resolve by
    /// V_SharedStateValidatorTests.Validate_PrimitiveSharedTypeId_NoBP2041) rather than a real
    /// Category-1 shared struct, so this fixture has zero dependency on Hrot.AI.Behaviors.
    /// </summary>
    private static BlueprintAsset BuildGetSetSharedMinimalAsset()
    {
        var getValuePin = DataPin("Value", "Out", "System.Int32");
        var getFoundPin = DataPin("Found", "Out", "System.Boolean");
        var getNode = new GetSharedNode { Id = Guid.NewGuid(), VariableId = "x", SharedTypeId = "System.Int32" };
        getNode.Pins.AddRange(new[] { getValuePin, getFoundPin });

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In",  "System.Int32");
        var setWritten  = DataPin("Written", "Out", "System.Boolean");
        var setNode = new SetSharedNode { Id = Guid.NewGuid(), VariableId = "x", SharedTypeId = "System.Int32" };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin, setWritten });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,    ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,  ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id, FromPinId = getValuePin.Id, ToNodeId = setNode.Id, ToPinId = setValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "GetSetSharedCoverage",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(FloatOut) -&gt; Return, fed by a data-only GetComponent node
    /// (P2 -- Hill-attack -&gt; Blueprints migration). Mirrors <see cref="BuildGetSetSharedMinimalAsset"/>'s
    /// shape/evidence bar. Uses <c>System.Numerics.Vector3</c> (public field "X") as the "component"
    /// type -- a real, already-resolvable, zero-Hrot.AI.Behaviors-dependency blittable struct --
    /// rather than a real ECS-registered component: <c>GetComponentRO&lt;T&gt;</c> only requires
    /// <c>T : unmanaged</c> at compile time (no registration check), so this exercises the SAME
    /// Stage5_Schedule GetComponentNode lowering (IrOp_Self -&gt; IrOp_GetComponentRO -&gt;
    /// IrOp_FieldRead) real components use, without pulling in Hrot.AI.Behaviors/Fdp.Toolkits. No
    /// "Target" pin is authored -- self-default path (unwired Target -&gt; IrOp_Self).
    /// </summary>
    private static BlueprintAsset BuildGetComponentMinimalAsset()
    {
        var getValuePin = DataPin("Value", "Out", "System.Single");
        var getNode = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            FieldName        = "X",
            FieldTypeFqn     = "System.Single",
        };
        getNode.Pins.Add(getValuePin);

        var floatVarId = Guid.NewGuid();
        var floatVar = new VariableDecl
        {
            Id   = floatVarId,
            Name = "FloatOut",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        };

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In", "System.Single");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = floatVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,    ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,  ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id, FromPinId = getValuePin.Id, ToNodeId = setNode.Id, ToPinId = setValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "GetComponentCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { floatVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetComponent(Fields=[X]; X &lt;- Literal 5.5f) -&gt; SetVariable(WrittenOut &lt;-
    /// SetComponent.Written) -&gt; Return (CA-03, Slice W1 -- unmanaged write). Mirrors <see
    /// cref="BuildGetComponentMinimalAsset"/>'s shape/evidence bar exactly: <c>System.Numerics.Vector3</c>
    /// (public, MUTABLE field "X") as the "component" type -- <c>GetComponentRW&lt;T&gt;</c>/
    /// <c>HasComponent&lt;T&gt;</c> only require <c>T : unmanaged</c> at compile time (no registration
    /// check), so this exercises the SAME Stage5_Schedule SetComponentNode lowering (IrOp_Self -&gt;
    /// IrOp_WriteComponentFields's HasComponent-guarded GetComponentRW) real components use, without
    /// pulling in Hrot.AI.Behaviors/Fdp.Toolkits.
    /// </summary>
    private static BlueprintAsset BuildSetComponentMinimalAsset()
    {
        var litValuePin = DataPin("Value", "Out", "System.Single");
        var litNode = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Single", ValueJson = "5.5f" };
        litNode.Pins.Add(litValuePin);

        var setExecIn  = ExecPin("In",  "In");
        var setExecOut = ExecPin("Out", "Out");
        var setXPin    = DataPin("X",       "In",  "System.Single");
        var setWritten = DataPin("Written", "Out", "System.Boolean");
        var setNode = new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields           = new List<ComponentFieldDecl>
            {
                new ComponentFieldDecl { Name = "X", TypeId = "System.Single" },
            },
        };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setXPin, setWritten });

        var boolVarId = Guid.NewGuid();
        var boolVar = new VariableDecl
        {
            Id   = boolVarId,
            Name = "WrittenOut",
            Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        };

        var setVarExecIn  = ExecPin("ExecIn",  "In");
        var setVarExecOut = ExecPin("ExecOut", "Out");
        var setVarValueIn = DataPin("Value",   "In", "System.Boolean");
        var setVarNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setVarNode.Pins.AddRange(new[] { setVarExecIn, setVarExecOut, setVarValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, litNode, setNode, setVarNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,      FromPinId = entryOut.Id,      ToNodeId = setNode.Id,    ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,     FromPinId = setExecOut.Id,    ToNodeId = setVarNode.Id, ToPinId = setVarExecIn.Id },
                new Link { FromNodeId = setVarNode.Id,  FromPinId = setVarExecOut.Id, ToNodeId = ret.Id,        ToPinId = retIn.Id },
                new Link { FromNodeId = litNode.Id,     FromPinId = litValuePin.Id,   ToNodeId = setNode.Id,    ToPinId = setXPin.Id },
                new Link { FromNodeId = setNode.Id,     FromPinId = setWritten.Id,    ToNodeId = setVarNode.Id, ToPinId = setVarValueIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "SetComponentCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { boolVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(BoolOut) -&gt; Return, fed by a data-only Compare node (GAP-12 --
    /// native comparison node, retires the HillAssault2NavOps.IsArrived stopgap). Mirrors
    /// <see cref="BuildGetComponentMinimalAsset"/>'s shape/evidence bar: A/B are two Literal
    /// <c>System.Single</c> operands (5f == 5f), the Compare node's "A"/"B"/"Result" pins are
    /// authored explicitly (mirrors GetComponentNode -- Stage0_Rehydrate's "node.Pins.Count > 0
    /// =&gt; skip" guard leaves fully-authored pin-less-node exceptions alone, so no enricher is
    /// needed), exercising the real Stage5_Schedule CompareNode lowering (ResolveDataPin(A/B) -&gt;
    /// IrOp_Compare -&gt; infix "==") through the full Roslyn+ALC pipeline.
    /// </summary>
    /// <summary>
    /// Convergent control flow (merge point) regression: a diamond where a SECOND Branch is reached
    /// from BOTH arms of the first (Branch1.True → Branch2, Branch1.False → Branch2). Before the
    /// merge-point scheduler fix, Branch2 was scheduled once per predecessor edge, emitting duplicate
    /// C# goto labels (CS0140) — real Roslyn compilation (FullRoslynPipeline) rejected it. With the
    /// fix, Branch2 (exec in-degree 2) gets a single shared block both arms jump to, so it compiles.
    /// </summary>
    private static BlueprintAsset BuildDiamondMergeMinimalAsset()
    {
        var litA = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Boolean", ValueJson = "true" };
        var litAOut = DataPin("Value", "Out", "System.Boolean");
        litA.Pins.Add(litAOut);

        var litB = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Boolean", ValueJson = "true" };
        var litBOut = DataPin("Value", "Out", "System.Boolean");
        litB.Pins.Add(litBOut);

        var b1In    = ExecPin("In",    "In");
        var b1True  = ExecPin("True",  "Out");
        var b1False = ExecPin("False", "Out");
        var b1Cond  = DataPin("Condition", "In", "System.Boolean");
        var branch1 = new BranchNode { Id = Guid.NewGuid() };
        branch1.Pins.AddRange(new[] { b1In, b1True, b1False, b1Cond });

        var b2In    = ExecPin("In",    "In");
        var b2True  = ExecPin("True",  "Out");
        var b2False = ExecPin("False", "Out");
        var b2Cond  = DataPin("Condition", "In", "System.Boolean");
        var branch2 = new BranchNode { Id = Guid.NewGuid() };   // MERGE POINT (in-degree 2)
        branch2.Pins.AddRange(new[] { b2In, b2True, b2False, b2Cond });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var retS   = new ReturnNode { Id = Guid.NewGuid() };
        var retSIn = ExecPin("ExecIn", "In");
        retS.Pins.Add(retSIn);

        var retF   = new ReturnNode { Id = Guid.NewGuid() };
        var retFIn = ExecPin("ExecIn", "In");
        retF.Pins.Add(retFIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, litA, litB, branch1, branch2, retS, retF },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id, ToNodeId = branch1.Id, ToPinId = b1In.Id },
                new Link { FromNodeId = litA.Id,    FromPinId = litAOut.Id,  ToNodeId = branch1.Id, ToPinId = b1Cond.Id },
                new Link { FromNodeId = litB.Id,    FromPinId = litBOut.Id,  ToNodeId = branch2.Id, ToPinId = b2Cond.Id },
                // Both arms of Branch1 converge on Branch2 (the merge point).
                new Link { FromNodeId = branch1.Id, FromPinId = b1True.Id,   ToNodeId = branch2.Id, ToPinId = b2In.Id },
                new Link { FromNodeId = branch1.Id, FromPinId = b1False.Id,  ToNodeId = branch2.Id, ToPinId = b2In.Id },
                new Link { FromNodeId = branch2.Id, FromPinId = b2True.Id,   ToNodeId = retS.Id,    ToPinId = retSIn.Id },
                new Link { FromNodeId = branch2.Id, FromPinId = b2False.Id,  ToNodeId = retF.Id,    ToPinId = retFIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "DiamondMergeCoverage",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    private static BlueprintAsset BuildCompareMinimalAsset()
    {
        var litAValuePin = DataPin("Value", "Out", "System.Single");
        var litA = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Single", ValueJson = "5f" };
        litA.Pins.Add(litAValuePin);

        var litBValuePin = DataPin("Value", "Out", "System.Single");
        var litB = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Single", ValueJson = "5f" };
        litB.Pins.Add(litBValuePin);

        var cmpAPin      = DataPin("A",      "In",  "System.Single");
        var cmpBPin      = DataPin("B",      "In",  "System.Single");
        var cmpResultPin = DataPin("Result", "Out", "System.Boolean");
        var cmpNode = new CompareNode { Id = Guid.NewGuid(), Operator = ComparisonOperator.Equal };
        cmpNode.Pins.AddRange(new[] { cmpAPin, cmpBPin, cmpResultPin });

        var boolVarId = Guid.NewGuid();
        var boolVar = new VariableDecl
        {
            Id   = boolVarId,
            Name = "BoolOut",
            Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        };

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In", "System.Boolean");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, litA, litB, cmpNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,   ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = litA.Id,    FromPinId = litAValuePin.Id, ToNodeId = cmpNode.Id, ToPinId = cmpAPin.Id },
                new Link { FromNodeId = litB.Id,    FromPinId = litBValuePin.Id, ToNodeId = cmpNode.Id, ToPinId = cmpBPin.Id },
                new Link { FromNodeId = cmpNode.Id, FromPinId = cmpResultPin.Id, ToNodeId = setNode.Id, ToPinId = setValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "CompareCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { boolVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(IntOut) -&gt; Return, fed by a data-only BinaryOp node (arithmetic
    /// `Compare` sibling). Mirrors <see cref="BuildCompareMinimalAsset"/>'s shape/evidence bar: A/B
    /// are two Literal <c>System.Int32</c> operands (2 + 3), the BinaryOp node's "A"/"B"/"Result"
    /// pins are authored explicitly (mirrors CompareNode/GetComponentNode -- Stage0_Rehydrate's
    /// "node.Pins.Count > 0 =&gt; skip" guard leaves fully-authored pin-less-node exceptions alone,
    /// so no enricher is needed), exercising the real Stage5_Schedule BinaryOpNode lowering
    /// (ResolveDataPin(A/B) -&gt; IrOp_BinaryOp -&gt; infix "+", result typed = A's Int32 type)
    /// through the full Roslyn+ALC pipeline.
    /// </summary>
    private static BlueprintAsset BuildBinaryOpMinimalAsset()
    {
        var litAValuePin = DataPin("Value", "Out", "System.Int32");
        var litA = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "2" };
        litA.Pins.Add(litAValuePin);

        var litBValuePin = DataPin("Value", "Out", "System.Int32");
        var litB = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "3" };
        litB.Pins.Add(litBValuePin);

        var binAPin      = DataPin("A",      "In",  "System.Int32");
        var binBPin      = DataPin("B",      "In",  "System.Int32");
        var binResultPin = DataPin("Result", "Out", "System.Int32");
        var binNode = new BinaryOpNode { Id = Guid.NewGuid(), Operator = ArithmeticOperator.Add };
        binNode.Pins.AddRange(new[] { binAPin, binBPin, binResultPin });

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl
        {
            Id   = intVarId,
            Name = "IntOut",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, litA, litB, binNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,   ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = litA.Id,    FromPinId = litAValuePin.Id, ToNodeId = binNode.Id, ToPinId = binAPin.Id },
                new Link { FromNodeId = litB.Id,    FromPinId = litBValuePin.Id, ToNodeId = binNode.Id, ToPinId = binBPin.Id },
                new Link { FromNodeId = binNode.Id, FromPinId = binResultPin.Id, ToNodeId = setNode.Id, ToPinId = setValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "BinaryOpCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(BoolOut) -&gt; Return, fed by a data-only BooleanOp node (Compare's
    /// boolean sibling). Mirrors <see cref="BuildBinaryOpMinimalAsset"/>'s shape/evidence bar: A/B
    /// are two Literal <c>System.Boolean</c> operands (<c>true &amp;&amp; false</c>), the BooleanOp
    /// node's "A"/"B"/"Result" pins are authored explicitly (mirrors CompareNode/BinaryOpNode --
    /// Stage0_Rehydrate's "node.Pins.Count > 0 =&gt; skip" guard leaves fully-authored pin-less-node
    /// exceptions alone, so no enricher is needed), exercising the real Stage5_Schedule
    /// BooleanOpNode lowering (ResolveDataPin(A/B) -&gt; IrOp_BooleanOp -&gt; infix "&amp;&amp;",
    /// result typed BoolType) through the full Roslyn+ALC pipeline.
    /// </summary>
    private static BlueprintAsset BuildBooleanOpMinimalAsset()
    {
        var litAValuePin = DataPin("Value", "Out", "System.Boolean");
        var litA = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Boolean", ValueJson = "true" };
        litA.Pins.Add(litAValuePin);

        var litBValuePin = DataPin("Value", "Out", "System.Boolean");
        var litB = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Boolean", ValueJson = "false" };
        litB.Pins.Add(litBValuePin);

        var boolOpAPin      = DataPin("A",      "In",  "System.Boolean");
        var boolOpBPin      = DataPin("B",      "In",  "System.Boolean");
        var boolOpResultPin = DataPin("Result", "Out", "System.Boolean");
        var boolOpNode = new BooleanOpNode { Id = Guid.NewGuid(), Operator = BooleanOperator.And };
        boolOpNode.Pins.AddRange(new[] { boolOpAPin, boolOpBPin, boolOpResultPin });

        var boolVarId = Guid.NewGuid();
        var boolVar = new VariableDecl
        {
            Id   = boolVarId,
            Name = "BoolOut",
            Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        };

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In", "System.Boolean");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, litA, litB, boolOpNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,      FromPinId = entryOut.Id,        ToNodeId = setNode.Id,    ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,    FromPinId = setExecOut.Id,      ToNodeId = ret.Id,        ToPinId = retIn.Id },
                new Link { FromNodeId = litA.Id,       FromPinId = litAValuePin.Id,    ToNodeId = boolOpNode.Id, ToPinId = boolOpAPin.Id },
                new Link { FromNodeId = litB.Id,       FromPinId = litBValuePin.Id,    ToNodeId = boolOpNode.Id, ToPinId = boolOpBPin.Id },
                new Link { FromNodeId = boolOpNode.Id, FromPinId = boolOpResultPin.Id, ToNodeId = setNode.Id,    ToPinId = setValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "BooleanOpCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { boolVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(BoolOut) -&gt; Return, fed by a data-only Not node (Compare's
    /// unary boolean sibling). Mirrors <see cref="BuildBooleanOpMinimalAsset"/>'s shape/evidence
    /// bar but with a SINGLE Literal <c>System.Boolean</c> operand (<c>!true</c>). The Not node's
    /// "A"/"Result" pins are authored explicitly (mirrors CompareNode/BinaryOpNode/BooleanOpNode --
    /// Stage0_Rehydrate's "node.Pins.Count > 0 =&gt; skip" guard leaves fully-authored pin-less-node
    /// exceptions alone, so no enricher is needed), exercising the real Stage5_Schedule NotNode
    /// lowering (ResolveDataPin(A) -&gt; IrOp_Not -&gt; prefix "!", result typed BoolType) through
    /// the full Roslyn+ALC pipeline.
    /// </summary>
    private static BlueprintAsset BuildNotMinimalAsset()
    {
        var litAValuePin = DataPin("Value", "Out", "System.Boolean");
        var litA = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Boolean", ValueJson = "true" };
        litA.Pins.Add(litAValuePin);

        var notAPin      = DataPin("A",      "In",  "System.Boolean");
        var notResultPin = DataPin("Result", "Out", "System.Boolean");
        var notNode = new NotNode { Id = Guid.NewGuid() };
        notNode.Pins.AddRange(new[] { notAPin, notResultPin });

        var boolVarId = Guid.NewGuid();
        var boolVar = new VariableDecl
        {
            Id   = boolVarId,
            Name = "BoolOut",
            Type = new BlueprintTypeRef { TypeId = "System.Boolean" },
        };

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In", "System.Boolean");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, litA, notNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,   ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = litA.Id,    FromPinId = litAValuePin.Id, ToNodeId = notNode.Id, ToPinId = notAPin.Id },
                new Link { FromNodeId = notNode.Id, FromPinId = notResultPin.Id, ToNodeId = setNode.Id, ToPinId = setValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "NotCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { boolVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(FloatOut) -&gt; Return, fed by a data-only GetParameter node
    /// (GAP-11). Mirrors <see cref="BuildGetComponentMinimalAsset"/>'s shape/evidence bar. Unlike
    /// that fixture, GetParameter's <c>Parameters</c> declaration is only legal under AiPrimitive
    /// dispatch (Stage2_Validate's BP1031 rejects a non-empty Parameters/WorkingState list on an
    /// Instance asset) -- so this fixture uses <c>Dispatch = AiPrimitive</c> with a minimal
    /// Primitive block, and stashes the SetVariable target under WorkingState (AiPrimitive
    /// forbids Variables -- BP1024) rather than Variables. The GetParameter node's "Value" out-pin
    /// is authored explicitly (mirrors GetComponentNode -- Stage0_Rehydrate's "node.Pins.Count > 0
    /// =&gt; skip" guard leaves fully-authored pin-less-node exceptions alone, so no enricher is
    /// needed), exercising the real Stage5_Schedule GetParameterNode lowering
    /// (FindParameterIndex -&gt; IrOp_ReadParam -&gt; `p.PIn`) through the full Roslyn+ALC pipeline.
    /// </summary>
    private static BlueprintAsset BuildGetParameterMinimalAsset()
    {
        var paramId = Guid.NewGuid();
        var param = new ParameterDecl
        {
            Id   = paramId,
            Name = "PIn",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        };

        var getValuePin = DataPin("Value", "Out", "System.Single");
        var getNode = new GetParameterNode { Id = Guid.NewGuid(), ParameterId = paramId.ToString() };
        getNode.Pins.Add(getValuePin);

        var floatVarId = Guid.NewGuid();
        var floatVar = new VariableDecl
        {
            Id   = floatVarId,
            Name = "FloatOut",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        };

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In", "System.Single");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = floatVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,    ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id, FromPinId = setExecOut.Id,  ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id, FromPinId = getValuePin.Id, ToNodeId = setNode.Id, ToPinId = setValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId      = Guid.NewGuid(),
            Name         = "GetParameterCoverage",
            Dispatch     = BlueprintDispatchKind.AiPrimitive,
            Primitive    = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = { AiPrimitiveHosting.BTreeAction },
            },
            Parameters   = { param },
            WorkingState = { floatVar },
            Graphs       = { graph },
        };
    }

    /// <summary>
    /// GetAllParametersNode -- near-exact mirror of <see cref="BuildGetParameterMinimalAsset"/>
    /// (same AiPrimitive/WorkingState shape, same evidence bar), but exercises the ONE-node,
    /// ONE-out-pin-per-Parameter shape: EventEntry -&gt; SetVariable(FloatOut) -&gt;
    /// SetVariable(IntOut) -&gt; Return, both SetVariable "Value" data-in pins fed by TWO DIFFERENT
    /// out-pins of a SINGLE GetAllParametersNode. Two Parameters (ParamA:Single, ParamB:Int32) are
    /// declared; the node's two data-out pins are authored explicitly (mirrors GetParameterNode --
    /// Stage0_Rehydrate's "node.Pins.Count &gt; 0 =&gt; skip" guard leaves fully-authored pin-less-node
    /// exceptions alone, so no enricher runs here), exercising the real Stage5_Schedule
    /// GetAllParametersNode lowering (per-pin FindParameterIndex-by-NAME -&gt; IrOp_ReadParam -&gt;
    /// `p.ParamA`/`p.ParamB`) through the full Roslyn+ALC pipeline.
    /// </summary>
    private static BlueprintAsset BuildGetAllParametersMinimalAsset()
    {
        var paramAId = Guid.NewGuid();
        var paramA = new ParameterDecl
        {
            Id   = paramAId,
            Name = "ParamA",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        };
        var paramBId = Guid.NewGuid();
        var paramB = new ParameterDecl
        {
            Id   = paramBId,
            Name = "ParamB",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var gapPinA = DataPin("ParamA", "Out", "System.Single");
        var gapPinB = DataPin("ParamB", "Out", "System.Int32");
        var gapNode = new GetAllParametersNode { Id = Guid.NewGuid() };
        gapNode.Pins.AddRange(new[] { gapPinA, gapPinB });

        var floatVarId = Guid.NewGuid();
        var floatVar = new VariableDecl
        {
            Id   = floatVarId,
            Name = "FloatOut",
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        };
        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl
        {
            Id   = intVarId,
            Name = "IntOut",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var setFloatExecIn   = ExecPin("ExecIn",  "In");
        var setFloatExecOut  = ExecPin("ExecOut", "Out");
        var setFloatValuePin = DataPin("Value",   "In", "System.Single");
        var setFloatNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = floatVarId.ToString() };
        setFloatNode.Pins.AddRange(new[] { setFloatExecIn, setFloatExecOut, setFloatValuePin });

        var setIntExecIn   = ExecPin("ExecIn",  "In");
        var setIntExecOut  = ExecPin("ExecOut", "Out");
        var setIntValuePin = DataPin("Value",   "In", "System.Int32");
        var setIntNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setIntNode.Pins.AddRange(new[] { setIntExecIn, setIntExecOut, setIntValuePin });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, gapNode, setFloatNode, setIntNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,        FromPinId = entryOut.Id,        ToNodeId = setFloatNode.Id, ToPinId = setFloatExecIn.Id },
                new Link { FromNodeId = setFloatNode.Id,  FromPinId = setFloatExecOut.Id, ToNodeId = setIntNode.Id,   ToPinId = setIntExecIn.Id },
                new Link { FromNodeId = setIntNode.Id,    FromPinId = setIntExecOut.Id,   ToNodeId = ret.Id,          ToPinId = retIn.Id },
                new Link { FromNodeId = gapNode.Id,       FromPinId = gapPinA.Id,         ToNodeId = setFloatNode.Id, ToPinId = setFloatValuePin.Id },
                new Link { FromNodeId = gapNode.Id,       FromPinId = gapPinB.Id,         ToNodeId = setIntNode.Id,   ToPinId = setIntValuePin.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId      = Guid.NewGuid(),
            Name         = "GetAllParametersCoverage",
            Dispatch     = BlueprintDispatchKind.AiPrimitive,
            Primitive    = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = { AiPrimitiveHosting.BTreeAction },
            },
            Parameters   = { paramA, paramB },
            WorkingState = { floatVar, intVar },
            Graphs       = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; PublishEvent("ClearBehaviorEvent") -&gt; Return (P4 -- GAP-3). Exercises the
    /// PublishEventNode Stage5 lowering (EngineEventCatalog lookup -&gt; IrOp_PublishBusEvent with the
    /// entry's TargetFieldName self-defaulted). AiPrimitive/Action dispatch (world.Bus is the AiPrimitive
    /// publish surface). No data pins: ClearBehaviorEvent's only field is the target Entity, which
    /// self-defaults from the unwired "Target" pin. Registered ValidateOnlyStage1To7 (see the
    /// yield-return comment) so it does not require the coverage-Roslyn compile to reference Fdp.Toolkits.
    /// </summary>
    private static BlueprintAsset BuildPublishEventMinimalAsset()
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var pubExecIn  = ExecPin("ExecIn",  "In");
        var pubExecOut = ExecPin("ExecOut", "Out");
        var pubNode = new PublishEventNode { Id = Guid.NewGuid(), EventId = "ClearBehaviorEvent" };
        pubNode.Pins.AddRange(new[] { pubExecIn, pubExecOut });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, pubNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,   ToNodeId = pubNode.Id, ToPinId = pubExecIn.Id },
                new Link { FromNodeId = pubNode.Id, FromPinId = pubExecOut.Id, ToNodeId = ret.Id,     ToPinId = retIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "PublishEventCoverage",
            Dispatch  = BlueprintDispatchKind.AiPrimitive,
            Primitive = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = { AiPrimitiveHosting.BTreeAction },
            },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; FlowForEach(Body -&gt; PublishEvent(Target &lt;- CurrentItem)) [Completed] -&gt; Return
    /// (P1 -- GAP-1). Exercises the FlowForEachNode Stage5 lowering (self/roster GetComponentRO + inline
    /// body scheduling + IrOp_ForEach) and the branch-free/latent-free body. AiPrimitive/Action dispatch.
    /// Registered ValidateOnlyStage1To7 (see the yield-return comment) so it does not need the
    /// coverage-Roslyn compile to reference UnitRosterOps/UnitRoster.
    /// </summary>
    private static BlueprintAsset BuildFlowForEachMinimalAsset()
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var feIn        = ExecPin("In", "In");
        var feBody      = ExecPin("Body", "Out");
        var feCompleted = ExecPin("Completed", "Out");
        var feItem      = DataPin("CurrentItem", "Out", "Fdp.Core.Entity");
        var fe = new FlowForEachNode
        {
            Id                 = Guid.NewGuid(),
            SourceComponentFqn = "Fdp.Core.CommandHierarchy.UnitRoster",
            CountAccessorFqn   = "Hrot.AI.Behaviors.Brains.UnitRosterOps.Count",
            ItemAccessorFqn    = "Hrot.AI.Behaviors.Brains.UnitRosterOps.Subordinate",
        };
        fe.Pins.AddRange(new[] { feIn, feBody, feCompleted, feItem });

        var pubIn     = ExecPin("In", "In");
        var pubOut    = ExecPin("Out", "Out");
        var pubTarget = DataPin("Target", "In", "Fdp.Core.Entity");
        var pub = new PublishEventNode { Id = Guid.NewGuid(), EventId = "ClearBehaviorEvent" };
        pub.Pins.AddRange(new[] { pubIn, pubOut, pubTarget });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("In", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, fe, pub, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id,    ToNodeId = fe.Id,  ToPinId = feIn.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feBody.Id,      ToNodeId = pub.Id, ToPinId = pubIn.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feItem.Id,      ToNodeId = pub.Id, ToPinId = pubTarget.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feCompleted.Id, ToNodeId = ret.Id, ToPinId = retIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "FlowForEachCoverage",
            Dispatch  = BlueprintDispatchKind.AiPrimitive,
            Primitive = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = { AiPrimitiveHosting.BTreeAction },
            },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; FlowForEach(Body -&gt; SetShared(int "scratch" &lt;- BinaryOp(CurrentIndex -
    /// Count))) [Completed] -&gt; Return. Exercises the FlowForEach loop-introspection out-pins
    /// (<c>CurrentIndex</c>, <c>Count</c>): the Count out wires into the arithmetic BinaryOp's B
    /// operand (proving the loop-invariant count is available in the body scope) and CurrentIndex into
    /// its A operand (proving the 0-based iteration index is available), so the emitted body reads both
    /// the outer-scope count local and the body-scope index local. Mirrors
    /// <see cref="BuildFlowForEachMinimalAsset"/>'s AiPrimitive shape; ValidateOnlyStage1To7 for the
    /// same game-assembly reason (UnitRosterOps/UnitRoster refs in the generated for-loop).
    /// </summary>
    private static BlueprintAsset BuildFlowForEachIndexCountMinimalAsset()
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var feIn        = ExecPin("In", "In");
        var feBody      = ExecPin("Body", "Out");
        var feCompleted = ExecPin("Completed", "Out");
        var feItem      = DataPin("CurrentItem",  "Out", "Fdp.Core.Entity");
        var feIndex     = DataPin("CurrentIndex", "Out", "System.Int32");
        var feCount     = DataPin("Count",        "Out", "System.Int32");
        var fe = new FlowForEachNode
        {
            Id                 = Guid.NewGuid(),
            SourceComponentFqn = "Fdp.Core.CommandHierarchy.UnitRoster",
            CountAccessorFqn   = "Hrot.AI.Behaviors.Brains.UnitRosterOps.Count",
            ItemAccessorFqn    = "Hrot.AI.Behaviors.Brains.UnitRosterOps.Subordinate",
        };
        fe.Pins.AddRange(new[] { feIn, feBody, feCompleted, feItem, feIndex, feCount });

        // index - count (arithmetic BinaryOp): A <- CurrentIndex, B <- Count.
        var binAPin      = DataPin("A",      "In",  "System.Int32");
        var binBPin      = DataPin("B",      "In",  "System.Int32");
        var binResultPin = DataPin("Result", "Out", "System.Int32");
        var binNode = new BinaryOpNode { Id = Guid.NewGuid(), Operator = ArithmeticOperator.Subtract };
        binNode.Pins.AddRange(new[] { binAPin, binBPin, binResultPin });

        // SetShared(int) inside the body consumes the BinaryOp result (keeps both loop outs live).
        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValuePin = DataPin("Value",   "In",  "System.Int32");
        var setWritten  = DataPin("Written", "Out", "System.Boolean");
        var setNode = new SetSharedNode { Id = Guid.NewGuid(), VariableId = "scratch", SharedTypeId = "System.Int32" };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValuePin, setWritten });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("In", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, fe, binNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = fe.Id,      ToPinId = feIn.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feBody.Id,       ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feIndex.Id,      ToNodeId = binNode.Id, ToPinId = binAPin.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feCount.Id,      ToNodeId = binNode.Id, ToPinId = binBPin.Id },
                new Link { FromNodeId = binNode.Id, FromPinId = binResultPin.Id, ToNodeId = setNode.Id, ToPinId = setValuePin.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feCompleted.Id,  ToNodeId = ret.Id,     ToPinId = retIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "FlowForEachIndexCountCoverage",
            Dispatch  = BlueprintDispatchKind.AiPrimitive,
            Primitive = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = { AiPrimitiveHosting.BTreeAction },
            },
            Graphs    = { graph },
        };
    }

    // ---- Component-collection consumer minimal fixtures (CA-07b) --------------

    private const string CcComponentFqn = "Hrot.AI.Behaviors.BpCollectionDemo";
    private const string CcCountFqn     = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count";
    private const string CcItemFqn      = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item";

    /// <summary>
    /// Builds a pin-authored <c>GetComponent&lt;BpCollectionDemo&gt;</c> node with a single baked
    /// collection decl ("Values", element System.Int32) -- multi-pin shape: "Values" (Out, IsArray)
    /// + "Found" (Out, bool). Shared by the three ComponentXxx coverage fixtures below.
    /// </summary>
    private static (GetComponentNode Node, Pin ValuesOut) BuildComponentCollectionSourceNode()
    {
        var valuesOut = DataPin("Values", "Out", "System.Int32");
        valuesOut.TypeRef.IsArray = true;
        var foundOut = DataPin("Found", "Out", "System.Boolean");
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = CcComponentFqn,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name             = "Values",
                    TypeId           = "",
                    IsCollection     = true,
                    ElementTypeId    = "System.Int32",
                    CountAccessorFqn = CcCountFqn,
                    ItemAccessorFqn  = CcItemFqn,
                },
            },
        };
        node.Pins.AddRange(new[] { valuesOut, foundOut });
        return (node, valuesOut);
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(CountOut &lt;- ComponentItemCount(Collection &lt;-
    /// GetComponent&lt;BpCollectionDemo&gt;.Values)) -&gt; Return. ValidateOnlyStage1To7 (same
    /// game-assembly reason as FlowForEach -- BpCollectionDemoOps lives in Hrot.AI.Behaviors).
    /// </summary>
    private static BlueprintAsset BuildComponentItemCountMinimalAsset()
    {
        var (getNode, valuesOut) = BuildComponentCollectionSourceNode();

        var collectionIn = DataPin("Collection", "In", "System.Int32");
        collectionIn.TypeRef.IsArray = true;
        var countOut = DataPin("Count", "Out", "System.Int32");
        var countNode = new ComponentItemCountNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = CcComponentFqn,
            CountAccessorFqn = CcCountFqn,
        };
        countNode.Pins.AddRange(new[] { collectionIn, countOut });

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl { Id = intVarId, Name = "CountOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, countNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,   ToNodeId = setNode.Id,   ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setExecOut.Id, ToNodeId = ret.Id,       ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,  FromPinId = valuesOut.Id,  ToNodeId = countNode.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = countNode.Id, FromPinId = countOut.Id,  ToNodeId = setNode.Id,   ToPinId = setValueIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ComponentItemCountCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; SetVariable(ElementOut &lt;- ComponentItemGet(Collection &lt;-
    /// GetComponent&lt;BpCollectionDemo&gt;.Values, Index &lt;- Literal 0)) -&gt; Return.
    /// ValidateOnlyStage1To7 -- same reason as <see cref="BuildComponentItemCountMinimalAsset"/>.
    /// </summary>
    private static BlueprintAsset BuildComponentItemGetMinimalAsset()
    {
        var (getNode, valuesOut) = BuildComponentCollectionSourceNode();

        var collectionIn = DataPin("Collection", "In", "System.Int32");
        collectionIn.TypeRef.IsArray = true;
        var indexIn    = DataPin("Index",   "In",  "System.Int32");
        var elementOut = DataPin("Element", "Out", "System.Int32");
        var getItemNode = new ComponentItemGetNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = CcComponentFqn,
            ItemAccessorFqn  = CcItemFqn,
            ElementTypeFqn   = "System.Int32",
        };
        getItemNode.Pins.AddRange(new[] { collectionIn, indexIn, elementOut });

        var litValueOut = DataPin("Value", "Out", "System.Int32");
        var litNode = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "0" };
        litNode.Pins.Add(litValueOut);

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl { Id = intVarId, Name = "ElementOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, litNode, getItemNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,      FromPinId = entryOut.Id,    ToNodeId = setNode.Id,     ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,    FromPinId = setExecOut.Id,  ToNodeId = ret.Id,         ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,    FromPinId = valuesOut.Id,   ToNodeId = getItemNode.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = litNode.Id,    FromPinId = litValueOut.Id, ToNodeId = getItemNode.Id, ToPinId = indexIn.Id },
                new Link { FromNodeId = getItemNode.Id, FromPinId = elementOut.Id, ToNodeId = setNode.Id,     ToPinId = setValueIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ComponentItemGetCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; ComponentForEach(Collection &lt;- GetComponent&lt;BpCollectionDemo&gt;.Values)
    /// [Body -&gt; SetVariable(ItemOut &lt;- CurrentItem)] [Completed -&gt; Return].
    /// ValidateOnlyStage1To7 -- same reason as <see cref="BuildComponentItemCountMinimalAsset"/>.
    /// </summary>
    private static BlueprintAsset BuildComponentForEachMinimalAsset()
    {
        var (getNode, valuesOut) = BuildComponentCollectionSourceNode();

        var feIn = ExecPin("In", "In");
        var feCollection = DataPin("Collection", "In", "System.Int32");
        feCollection.TypeRef.IsArray = true;
        var feBody      = ExecPin("Body", "Out");
        var feCompleted = ExecPin("Completed", "Out");
        var feItem      = DataPin("CurrentItem", "Out", "System.Int32");
        var fe = new ComponentForEachNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = CcComponentFqn,
            CountAccessorFqn = CcCountFqn,
            ItemAccessorFqn  = CcItemFqn,
            ElementTypeFqn   = "System.Int32",
        };
        fe.Pins.AddRange(new[] { feIn, feCollection, feBody, feCompleted, feItem });

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl { Id = intVarId, Name = "ItemOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("In", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, fe, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,    ToNodeId = fe.Id,      ToPinId = feIn.Id },
                new Link { FromNodeId = getNode.Id, FromPinId = valuesOut.Id,   ToNodeId = fe.Id,      ToPinId = feCollection.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feBody.Id,      ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feItem.Id,      ToNodeId = setNode.Id, ToPinId = setValueIn.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feCompleted.Id, ToNodeId = ret.Id,     ToPinId = retIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ComponentForEachCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };
    }
}

using System.Reflection;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

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
        yield return ("Inline/GetParameter", new[] { BuildGetParameterMinimalAsset() }, null, CoverageMode.FullRoslynPipeline);
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
}

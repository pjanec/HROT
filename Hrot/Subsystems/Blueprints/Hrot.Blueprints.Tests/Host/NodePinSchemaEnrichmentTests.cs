using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Reload;
using Xunit;
// P7 -- top-level (non-nested) probe helpers shared with P7_FunctionCallContextTests; must be
// top-level so Type.FullName is dotted (nested types use "+", invalid as emitted C# source).
using P7ProbeHelpers = Hrot.Blueprints.Tests.Compiler.P7ProbeHelpers;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BCP-BATCH-03 — data/value pin enrichment for blueprint node kinds.
/// Exercises <see cref="NodePinSchema.GetCanonicalPins"/> directly (internal, visible via
/// InternalsVisibleTo) on bare nodes with empty <c>Pins</c> so the canonical schema path runs.
/// All tests are headless (no ImGui).  Every assertion checks real pin name + direction + type,
/// not non-null.
/// </summary>
public sealed class NodePinSchemaEnrichmentTests
{
    // ── test fixtures ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reflection targets for the FunctionCall and ChannelCommand tests.  Kept on a nested
    /// non-test type so xUnit does not flag the public methods (xUnit1013).
    /// </summary>
    public static class ReflectionTargets
    {
        /// <summary>A multi-field params DTO used to verify ChannelCommand param decomposition.</summary>
        public struct FakeFireParams
        {
            public int   Target;
            public float CooldownSeconds;
        }

        /// <summary>A static method with parameters + a non-void return for FunctionCall reflection.</summary>
        public static int AddOffset(int value, float scale) => value + (int)scale;

        /// <summary>A static void method to verify Return pin is omitted for void methods.</summary>
        public static void DoNothing(int value) { _ = value; }
    }

    // P7's trailing-context probe helpers (Probe/ProbeSelfOnly/ProbeViewOnly/ProbeEntityNotNamedSelf)
    // live on the TOP-LEVEL Hrot.Blueprints.Tests.Compiler.P7ProbeHelpers, not on the nested
    // ReflectionTargets above -- a nested type's Type.FullName uses "+" for the nested separator,
    // which is not valid emitted C# syntax for a `global::...` static-method-call reference (this
    // matters for P7_FunctionCallContextTests' full-Roslyn-compile E2E test; the pin-projection
    // tests below only need reflection, but share the same top-level class for a single source of
    // truth). See Hrot.Blueprints.Tests.Compiler.P7ProbeHelpers.

    /// <summary>Channel-command catalog stub that maps one action to <see cref="FakeFireParams"/>.</summary>
    private sealed class StubChannelCommandCatalog : IChannelCommandCatalog
    {
        private readonly IReadOnlyList<ChannelCommandCatalogEntry> _entries;
        public StubChannelCommandCatalog(params ChannelCommandCatalogEntry[] entries) => _entries = entries;
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() => _entries;
    }

    private static IPinAssertHelpers.PinView[] Data(IEnumerable<Pin> pins, string dir)
        => pins.Where(p => !p.IsExec && p.Direction == dir)
               .Select(p => new IPinAssertHelpers.PinView(p.Name, p.Direction, p.TypeRef?.TypeId ?? ""))
               .ToArray();

    private static bool HasExec(IEnumerable<Pin> pins, string name, string dir)
        => pins.Any(p => p.IsExec && p.Name == name && p.Direction == dir);

    // ── Task 1: ChannelCommandNode ─────────────────────────────────────────────

    [Fact]
    public void ChannelCommand_KnownAction_MultiFieldParams_ProjectsOnePinPerField_PlusExec()
    {
        var fqn = typeof(ReflectionTargets.FakeFireParams).FullName!;
        var catalog = new StubChannelCommandCatalog(
            new ChannelCommandCatalogEntry("Fire", "Some.Ns.WeaponChannel", 1, fqn));
        var node = new ChannelCommandNode { ChannelType = "WeaponChannel", ActionId = "Fire" };

        var pins = NodePinSchema.GetCanonicalPins(node, channelCommands: catalog);

        Assert.True(HasExec(pins, "In",  "In"),  "exec In missing");
        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");

        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        Assert.Contains(dataIn, p => p.Name == "Target"          && p.TypeId == "System.Int32");
        Assert.Contains(dataIn, p => p.Name == "CooldownSeconds" && p.TypeId == "System.Single");
        // No spurious data-out pins.
        Assert.Empty(Data(pins, "Out"));
    }

    [Fact]
    public void ChannelCommand_KnownAction_PrimitiveParams_ProjectsSingleValuePin()
    {
        // System.Int32 has no decomposable members → a single value pin typed as the params type.
        var catalog = new StubChannelCommandCatalog(
            new ChannelCommandCatalogEntry("MoveTo", "Fdp.X.LocomotionChannel", 1, "System.Int32"));
        var node = new ChannelCommandNode { ChannelType = "LocomotionChannel", ActionId = "MoveTo" };

        var pins = NodePinSchema.GetCanonicalPins(node, channelCommands: catalog);

        var dataIn = Data(pins, "In");
        var single = Assert.Single(dataIn);
        Assert.Equal("Int32", single.Name);          // last segment of System.Int32
        Assert.Equal("System.Int32", single.TypeId);
        Assert.True(HasExec(pins, "In", "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    [Fact]
    public void ChannelCommand_UnknownAction_ExecOnly_NoThrow()
    {
        var catalog = new StubChannelCommandCatalog(
            new ChannelCommandCatalogEntry("Fire", "Some.Ns.WeaponChannel", 1, typeof(ReflectionTargets.FakeFireParams).FullName!));
        var node = new ChannelCommandNode { ChannelType = "WeaponChannel", ActionId = "NoSuchAction" };

        var pins = NodePinSchema.GetCanonicalPins(node, channelCommands: catalog);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In", "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    [Fact]
    public void ChannelCommand_NullCatalog_ExecOnly()
    {
        var node = new ChannelCommandNode { ChannelType = "WeaponChannel", ActionId = "Fire" };

        var pins = NodePinSchema.GetCanonicalPins(node, channelCommands: null);

        Assert.Empty(Data(pins, "In"));
        Assert.True(HasExec(pins, "In", "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    // ── Task 2: FunctionCallNode ────────────────────────────────────────────────

    [Fact]
    public void FunctionCall_NonPure_KnownStaticMethod_ProjectsParams_Return_AndExec()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(ReflectionTargets).FullName!,
            MethodName   = nameof(ReflectionTargets.AddOffset),
            IsPure       = false,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.True(HasExec(pins, "In",  "In"),  "non-pure must have exec In");
        Assert.True(HasExec(pins, "Out", "Out"), "non-pure must have exec Out");

        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        // Parameter order preserved (Stage5 ResolveAllDataInputs depends on declaration order).
        Assert.Equal("value", dataIn[0].Name);
        Assert.Equal("System.Int32", dataIn[0].TypeId);
        Assert.Equal("scale", dataIn[1].Name);
        Assert.Equal("System.Single", dataIn[1].TypeId);

        var ret = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Return", ret.Name);
        Assert.Equal("System.Int32", ret.TypeId);
    }

    [Fact]
    public void FunctionCall_Pure_KnownMethod_NoExecPins_ButParamsAndReturn()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(ReflectionTargets).FullName!,
            MethodName   = nameof(ReflectionTargets.AddOffset),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.False(pins.Any(p => p.IsExec), "pure function must have no exec pins");
        Assert.Equal(2, Data(pins, "In").Length);
        Assert.Single(Data(pins, "Out"));
    }

    [Fact]
    public void FunctionCall_VoidReturn_OmitsReturnPin()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(ReflectionTargets).FullName!,
            MethodName   = nameof(ReflectionTargets.DoNothing),
            IsPure       = false,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.Empty(Data(pins, "Out"));            // no Return pin for void
        Assert.Single(Data(pins, "In"));            // one param
        Assert.True(HasExec(pins, "Out", "Out"));   // exec Out still present
    }

    [Fact]
    public void FunctionCall_UnknownType_GracefulFallback_ExecOnly()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = "No.Such.Type.Anywhere",
            MethodName   = "Whatever",
            IsPure       = false,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In", "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    // ── P7: FunctionCall trailing engine-context recognition ────────────────────

    /// <summary>
    /// P7 -- a helper ending with `Entity self, ISimulationView view` projects ONLY the leading
    /// `x` data-IN pin; both trailing context params are omitted from the visual pins (auto-
    /// appended by the compiler at emit time instead). Default dispatch context (asset: null)
    /// assumes the common Instance/AiPrimitive case.
    /// </summary>
    [Fact]
    public void FunctionCall_TrailingSelfAndView_OmitsBothFromDataPins()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(P7ProbeHelpers).FullName!,
            MethodName   = nameof(P7ProbeHelpers.Probe),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        var dataIn = Data(pins, "In");
        var single = Assert.Single(dataIn);
        Assert.Equal("x", single.Name);
        Assert.Equal("System.Int32", single.TypeId);

        var ret = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Return", ret.Name);
        Assert.Equal("System.Int32", ret.TypeId);
    }

    /// <summary>P7 -- trailing `Entity self` only: omitted, leaving just `x`.</summary>
    [Fact]
    public void FunctionCall_TrailingSelfOnly_OmitsFromDataPins()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(P7ProbeHelpers).FullName!,
            MethodName   = nameof(P7ProbeHelpers.ProbeSelfOnly),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        var single = Assert.Single(Data(pins, "In"));
        Assert.Equal("x", single.Name);
    }

    /// <summary>P7 -- trailing `ISimulationView` only (any param name): omitted, leaving just `x`.</summary>
    [Fact]
    public void FunctionCall_TrailingViewOnly_OmitsFromDataPins()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(P7ProbeHelpers).FullName!,
            MethodName   = nameof(P7ProbeHelpers.ProbeViewOnly),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        var single = Assert.Single(Data(pins, "In"));
        Assert.Equal("x", single.Name);
    }

    /// <summary>
    /// P7 regression guard: a trailing <c>Entity</c> param NOT named "self" is NOT recognized as
    /// context -- both `x` and `target` remain ordinary wireable data-IN pins (no change from
    /// pre-P7 behavior for this shape).
    /// </summary>
    [Fact]
    public void FunctionCall_TrailingEntityNotNamedSelf_NotRecognized_BothPinsProjected()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(P7ProbeHelpers).FullName!,
            MethodName   = nameof(P7ProbeHelpers.ProbeEntityNotNamedSelf),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        Assert.Equal("x",      dataIn[0].Name);
        Assert.Equal("target", dataIn[1].Name);
        Assert.Equal("Fdp.Core.Entity", dataIn[1].TypeId);
    }

    /// <summary>
    /// P7 regression guard: a helper with NO trailing context params (existing
    /// <see cref="ReflectionTargets.AddOffset"/>) still projects both params -- unchanged from the
    /// pre-P7 assertions in <see cref="FunctionCall_Pure_KnownMethod_NoExecPins_ButParamsAndReturn"/>.
    /// Re-asserted here under the P7 section for visibility.
    /// </summary>
    [Fact]
    public void FunctionCall_NoTrailingContext_AllParamsStillProjected_NoRegression()
    {
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(ReflectionTargets).FullName!,
            MethodName   = nameof(ReflectionTargets.AddOffset),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.Equal(2, Data(pins, "In").Length);
        Assert.Single(Data(pins, "Out"));
    }

    /// <summary>
    /// P7 -- Library-dispatch gating: when the asset's dispatch is <c>Library</c> (no self/view in
    /// scope in the generated stateless static method), trailing-context recognition is suppressed
    /// and both params are projected as ordinary data-IN pins (pre-P7 behavior, unchanged).
    /// </summary>
    [Fact]
    public void FunctionCall_TrailingSelfAndView_LibraryDispatch_RecognitionSuppressed()
    {
        var asset = new BlueprintAsset { Dispatch = BlueprintDispatchKind.Library };
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(P7ProbeHelpers).FullName!,
            MethodName   = nameof(P7ProbeHelpers.Probe),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        // All 3 params projected: x, self, view -- context recognition suppressed for Library.
        var dataIn = Data(pins, "In");
        Assert.Equal(3, dataIn.Length);
        Assert.Equal("x", dataIn[0].Name);
    }

    /// <summary>
    /// P7 -- Instance-dispatch (explicit asset) recognizes trailing context exactly like the
    /// asset:null default, confirming the dispatch check reads <c>asset.Dispatch</c> correctly.
    /// </summary>
    [Fact]
    public void FunctionCall_TrailingSelfAndView_InstanceDispatch_Recognized()
    {
        var asset = new BlueprintAsset { Dispatch = BlueprintDispatchKind.Instance };
        var node = new FunctionCallNode
        {
            TargetTypeId = typeof(P7ProbeHelpers).FullName!,
            MethodName   = nameof(P7ProbeHelpers.Probe),
            IsPure       = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        var single = Assert.Single(Data(pins, "In"));
        Assert.Equal("x", single.Name);
    }

    // ── Task 3: static data pins ────────────────────────────────────────────────

    [Fact]
    public void Delay_HasDurationDataIn_AndExec()
    {
        var pins = NodePinSchema.GetCanonicalPins(new LatentDelayNode());

        var duration = Assert.Single(Data(pins, "In"));
        Assert.Equal("Duration", duration.Name);
        Assert.Equal("System.Single", duration.TypeId);
        Assert.True(HasExec(pins, "In", "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    [Fact]
    public void ScoreDecision_HasWinningOptionIdDataOut_AndExec()
    {
        var pins = NodePinSchema.GetCanonicalPins(new ScoreDecisionNode());

        var win = Assert.Single(Data(pins, "Out"));
        Assert.Equal("WinningOptionId", win.Name);
        Assert.Equal("System.Byte", win.TypeId);
        Assert.True(HasExec(pins, "In", "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    [Fact]
    public void ArrayGet_HasArray_Index_Element_Pins_InCompilerOrder()
    {
        var pins = NodePinSchema.GetCanonicalPins(new ArrayGetNode());

        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        // Array MUST be the first data-IN pin (Stage4_TypeResolve uses first data-IN as the array).
        Assert.Equal("Array", dataIn[0].Name);
        Assert.Equal("Index", dataIn[1].Name);
        Assert.Equal("System.Int32", dataIn[1].TypeId);

        var element = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Element", element.Name);
        Assert.True(HasExec(pins, "In", "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    [Fact]
    public void ArrayMake_HasElementInPins_AndArrayOut_TypedFromElementTypeId()
    {
        var pins = NodePinSchema.GetCanonicalPins(new ArrayMakeNode { ElementTypeId = "System.Int32" });

        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        Assert.Equal("0", dataIn[0].Name);
        Assert.Equal("System.Int32", dataIn[0].TypeId);
        Assert.Equal("1", dataIn[1].Name);
        Assert.Equal("System.Int32", dataIn[1].TypeId);

        var array = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Array", array.Name);
        Assert.Equal("System.Int32[]", array.TypeId);
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    // ── Branch Condition (verified compiler-consumed) ───────────────────────────

    [Fact]
    public void Branch_HasConditionDataIn_PlusExecInTrueFalse()
    {
        var pins = NodePinSchema.GetCanonicalPins(new BranchNode());

        var cond = Assert.Single(Data(pins, "In"));
        Assert.Equal("Condition", cond.Name);
        Assert.Equal("System.Boolean", cond.TypeId);

        Assert.True(HasExec(pins, "In",    "In"));
        Assert.True(HasExec(pins, "True",  "Out"));
        Assert.True(HasExec(pins, "False", "Out"));
    }

    // ── BATCH-02 Gap 1: ReadRankedResultNode ────────────────────────────────────

    /// <summary>
    /// ReadRankedResult must expose exactly the three struct fields emitted by
    /// InstanceEmitter.EmitReadRankedResultHelpers (lines 539-541) as data-OUT pins,
    /// because Stage5_Schedule.cs:1049-1062 does IrOp_FieldRead by pin name.
    /// </summary>
    [Fact]
    public void ReadRankedResult_HasThreeDataOutPins_IsValid_Entity_Score_NoExecNoDataIn()
    {
        var pins = NodePinSchema.GetCanonicalPins(new ReadRankedResultNode());

        // No exec pins — ReadRankedResult is a pure data-output node.
        Assert.False(pins.Any(p => p.IsExec), "ReadRankedResult must have no exec pins");

        // No data-IN pins — Rank is a node field baked at compile time (Stage5:1039).
        Assert.Empty(Data(pins, "In"));

        // Exactly 3 data-OUT pins in order: IsValid, Entity, Score.
        var dataOut = Data(pins, "Out");
        Assert.Equal(3, dataOut.Length);

        Assert.Equal("IsValid", dataOut[0].Name);
        Assert.Equal("System.Boolean", dataOut[0].TypeId);

        Assert.Equal("Entity", dataOut[1].Name);
        Assert.Equal("System.Int64", dataOut[1].TypeId);

        Assert.Equal("Score", dataOut[2].Name);
        Assert.Equal("System.Single", dataOut[2].TypeId);
    }

    // ── BATCH-02 Gap 2: CallCustomEventNode ─────────────────────────────────────

    /// <summary>
    /// CallCustomEvent with a declared event that has two typed parameters must yield
    /// exec In/Out + those two data-IN pins in declaration order.
    /// Grounded in Stage5_Schedule.cs:695-703 (ResolveAllDataInputs → IrOp_RaiseCustomEvent).
    /// </summary>
    [Fact]
    public void CallCustomEvent_KnownEvent_TwoParams_ProjectsExecAndDataInPinsInOrder()
    {
        var eventId = Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            CustomEvents = new List<CustomEventDecl>
            {
                new CustomEventDecl
                {
                    Id   = eventId,
                    Name = "OnTargetAcquired",
                    Parameters = new List<ParameterDecl>
                    {
                        new ParameterDecl { Name = "TargetEntity", Type = new BlueprintTypeRef { TypeId = "System.Int64"  } },
                        new ParameterDecl { Name = "Confidence",   Type = new BlueprintTypeRef { TypeId = "System.Single" } },
                    },
                },
            },
        };
        var node = new CallCustomEventNode { EventId = eventId.ToString() };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        Assert.True(HasExec(pins, "In",  "In"),  "exec In missing");
        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");

        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        // Declaration order preserved (Stage5 ResolveAllDataInputs reads pins positionally).
        Assert.Equal("TargetEntity", dataIn[0].Name);
        Assert.Equal("System.Int64",  dataIn[0].TypeId);
        Assert.Equal("Confidence",   dataIn[1].Name);
        Assert.Equal("System.Single", dataIn[1].TypeId);

        Assert.Empty(Data(pins, "Out")); // no data-OUT pins
    }

    /// <summary>
    /// CallCustomEvent graceful fallback when asset is null: exec-only, no throw.
    /// </summary>
    [Fact]
    public void CallCustomEvent_NullAsset_FallsBackToExecOnly()
    {
        var node = new CallCustomEventNode { EventId = Guid.NewGuid().ToString() };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: null);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    /// <summary>
    /// CallCustomEvent graceful fallback when EventId is not a valid Guid: exec-only.
    /// </summary>
    [Fact]
    public void CallCustomEvent_InvalidEventId_FallsBackToExecOnly()
    {
        var asset = new BlueprintAsset();
        var node  = new CallCustomEventNode { EventId = "not-a-guid" };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    /// <summary>
    /// CallCustomEvent graceful fallback when EventId parses but no matching event in asset:
    /// exec-only.
    /// </summary>
    [Fact]
    public void CallCustomEvent_EventNotFound_FallsBackToExecOnly()
    {
        var asset = new BlueprintAsset(); // empty CustomEvents
        var node  = new CallCustomEventNode { EventId = Guid.NewGuid().ToString() };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    // ── BATCH-02 Gap 3: CallPeerBlueprintNode ───────────────────────────────────

    /// <summary>
    /// CallPeerBlueprint must expose exec In/Out + a single Return data-OUT pin (System.Object).
    /// Grounded in Stage5_Schedule.cs:661: the compiler reads the first data-OUT pin as the
    /// return-value slot.  Dynamic data-IN argument pins are deferred to BATCH-03.
    /// </summary>
    [Fact]
    public void CallPeerBlueprint_HasExecInOut_AndSingleReturnDataOut_TypedSystemObject()
    {
        var pins = NodePinSchema.GetCanonicalPins(new CallPeerBlueprintNode());

        Assert.True(HasExec(pins, "In",  "In"),  "exec In missing");
        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");

        // No data-IN pins in this batch (deferred to BATCH-03).
        Assert.Empty(Data(pins, "In"));

        var ret = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Object", ret.TypeId);
    }

    // ── BATCH-03C: EventEntryNode in Function graph ─────────────────────────────

    /// <summary>
    /// EventEntryNode in a Function graph with 2 inputs → exec-Out + 2 data-Out pins,
    /// each named and typed from Graph.Inputs.
    /// Satisfies Stage5_Schedule.cs~1157-1189: compiler reads !IsExec &amp;&amp; Direction=="Out" pins
    /// and name-matches to Graph.Inputs.
    /// </summary>
    [Fact]
    public void EventEntryNode_FunctionGraph_TwoInputs_ProjectsExecOutPlusTwoDataOut()
    {
        var graphId = Guid.NewGuid();
        var graph = new Graph
        {
            Id   = graphId,
            Kind = GraphKind.Function,
            Inputs = new List<ParameterDecl>
            {
                new ParameterDecl { Name = "Target",     Type = new BlueprintTypeRef { TypeId = "System.Int64"   } },
                new ParameterDecl { Name = "Confidence", Type = new BlueprintTypeRef { TypeId = "System.Single"  } },
            },
        };
        var node = new EventEntryNode();

        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: graph);

        // Exactly one exec-Out pin.
        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");
        Assert.False(HasExec(pins, "In", "In"), "EventEntry must not have exec In");

        // Two data-Out pins in declaration order.
        var dataOut = Data(pins, "Out");
        Assert.Equal(2, dataOut.Length);
        Assert.Equal("Target",      dataOut[0].Name);
        Assert.Equal("System.Int64",  dataOut[0].TypeId);
        Assert.Equal("Confidence",  dataOut[1].Name);
        Assert.Equal("System.Single", dataOut[1].TypeId);

        // No data-In pins.
        Assert.Empty(Data(pins, "In"));
    }

    /// <summary>
    /// EventEntryNode in an Event graph (non-Function) → exec-only (unchanged behavior).
    /// </summary>
    [Fact]
    public void EventEntryNode_EventGraph_ExecOnly()
    {
        var graph = new Graph { Kind = GraphKind.Event };
        var node  = new EventEntryNode();

        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: graph);

        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");
        Assert.Empty(Data(pins, "Out"));
        Assert.Empty(Data(pins, "In"));
    }

    /// <summary>
    /// EventEntryNode in a Function graph with NO inputs → exec-only (unchanged behavior).
    /// </summary>
    [Fact]
    public void EventEntryNode_FunctionGraph_NoInputs_ExecOnly()
    {
        var graph = new Graph { Kind = GraphKind.Function, Inputs = new List<ParameterDecl>() };
        var node  = new EventEntryNode();

        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: graph);

        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");
        Assert.Empty(Data(pins, "Out"));
        Assert.Empty(Data(pins, "In"));
    }

    /// <summary>
    /// EventEntryNode with containingGraph=null → exec-only (graceful fallback).
    /// </summary>
    [Fact]
    public void EventEntryNode_NullGraph_ExecOnly()
    {
        var node = new EventEntryNode();

        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: null);

        Assert.True(HasExec(pins, "Out", "Out"));
        Assert.Empty(Data(pins, "Out"));
        Assert.Empty(Data(pins, "In"));
    }

    // ── BATCH-03C: ReturnNode in Function graph ─────────────────────────────────

    /// <summary>
    /// ReturnNode in a Function graph with 1 output → exec-In + 1 data-Out pin (Direction=="Out")
    /// named and typed from Graph.Outputs[0].
    /// Satisfies Stage5_Schedule.cs~881-897 BuildReturnTerminator:
    /// <c>rn.Pins.FirstOrDefault(p =&gt; !p.IsExec &amp;&amp; p.Direction == "Out")</c>.
    /// </summary>
    [Fact]
    public void ReturnNode_FunctionGraph_OneOutput_ProjectsExecInPlusDataOut()
    {
        var graph = new Graph
        {
            Kind = GraphKind.Function,
            Outputs = new List<ParameterDecl>
            {
                new ParameterDecl { Name = "Result", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
        };
        var node = new ReturnNode();

        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: graph);

        Assert.True(HasExec(pins, "In", "In"), "exec In missing");
        Assert.False(HasExec(pins, "Out", "Out"), "ReturnNode must not have exec Out");

        // The value pin MUST have Direction=="Out" (compiler contract: BuildReturnTerminator reads
        // !IsExec && Direction=="Out").
        var dataOut = Data(pins, "Out");
        var ret = Assert.Single(dataOut);
        Assert.Equal("Result",       ret.Name);
        Assert.Equal("System.Int32", ret.TypeId);
        Assert.Equal("Out",          ret.Direction); // critical: compiler reads data-OUT not data-IN

        // No data-In pins.
        Assert.Empty(Data(pins, "In"));
    }

    /// <summary>
    /// ReturnNode in a Function graph with NO outputs → exec-only (unchanged behavior).
    /// </summary>
    [Fact]
    public void ReturnNode_FunctionGraph_NoOutputs_ExecOnly()
    {
        var graph = new Graph { Kind = GraphKind.Function, Outputs = new List<ParameterDecl>() };
        var node  = new ReturnNode();

        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: graph);

        Assert.True(HasExec(pins, "In", "In"), "exec In missing");
        Assert.Empty(Data(pins, "Out"));
        Assert.Empty(Data(pins, "In"));
    }

    /// <summary>
    /// ReturnNode with containingGraph=null → exec-only (graceful fallback).
    /// </summary>
    [Fact]
    public void ReturnNode_NullGraph_ExecOnly()
    {
        var node = new ReturnNode();

        var pins = NodePinSchema.GetCanonicalPins(node, containingGraph: null);

        Assert.True(HasExec(pins, "In", "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.Empty(Data(pins, "In"));
    }

    // ── BATCH-03C: FunctionCallNode with TargetGraphId ─────────────────────────

    /// <summary>
    /// FunctionCall with TargetGraphId pointing to a Function graph (impure):
    /// exec In/Out + data-IN per target.Inputs + 1 data-OUT for target.Outputs[0].
    /// Satisfies Stage5_Schedule.cs~642-679:
    /// - ResolveAllDataInputs consumes data-IN pins positionally as call arguments.
    /// - gcOutPin = first !IsExec &amp;&amp; Direction=="Out" pin as return slot.
    /// </summary>
    [Fact]
    public void FunctionCall_TargetGraphId_ImpureFunction_ProjectsExecAndDataPins()
    {
        var targetGraphId = Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            Graphs = new List<Graph>
            {
                new Graph
                {
                    Id   = targetGraphId,
                    Kind = GraphKind.Function,
                    Inputs = new List<ParameterDecl>
                    {
                        new ParameterDecl { Name = "Threshold", Type = new BlueprintTypeRef { TypeId = "System.Single" } },
                        new ParameterDecl { Name = "Count",     Type = new BlueprintTypeRef { TypeId = "System.Int32"  } },
                    },
                    Outputs = new List<ParameterDecl>
                    {
                        new ParameterDecl { Name = "Score", Type = new BlueprintTypeRef { TypeId = "System.Single" } },
                    },
                },
            },
        };
        var node = new FunctionCallNode
        {
            TargetGraphId = targetGraphId.ToString(),
            IsPure        = false,
        };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        // Exec In/Out for impure call.
        Assert.True(HasExec(pins, "In",  "In"),  "exec In missing");
        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");

        // Data-IN pins in declaration order (Stage5 ResolveAllDataInputs reads positionally).
        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        Assert.Equal("Threshold",    dataIn[0].Name);
        Assert.Equal("System.Single", dataIn[0].TypeId);
        Assert.Equal("Count",        dataIn[1].Name);
        Assert.Equal("System.Int32",  dataIn[1].TypeId);

        // Single data-OUT pin for return value.
        var dataOut = Data(pins, "Out");
        var ret = Assert.Single(dataOut);
        Assert.Equal("Score",        ret.Name);
        Assert.Equal("System.Single", ret.TypeId);
    }

    /// <summary>
    /// FunctionCall with TargetGraphId (pure variant): no exec pins, only data-IN + data-OUT.
    /// </summary>
    [Fact]
    public void FunctionCall_TargetGraphId_PureFunction_NoExecPins()
    {
        var targetGraphId = Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            Graphs = new List<Graph>
            {
                new Graph
                {
                    Id   = targetGraphId,
                    Kind = GraphKind.Function,
                    Inputs = new List<ParameterDecl>
                    {
                        new ParameterDecl { Name = "Value", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                    Outputs = new List<ParameterDecl>
                    {
                        new ParameterDecl { Name = "Result", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
            },
        };
        var node = new FunctionCallNode
        {
            TargetGraphId = targetGraphId.ToString(),
            IsPure        = true,
        };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        Assert.False(pins.Any(p => p.IsExec), "pure function must have no exec pins");
        Assert.Single(Data(pins, "In"));
        Assert.Single(Data(pins, "Out"));
    }

    /// <summary>
    /// FunctionCall with unknown TargetGraphId (GUID doesn't match any graph in asset):
    /// falls through to CLR-reflection path gracefully (no throw).
    /// </summary>
    [Fact]
    public void FunctionCall_TargetGraphId_UnknownGuid_FallsBackToCLRPath_NoThrow()
    {
        var asset = new BlueprintAsset { Graphs = new List<Graph>() }; // empty graph list
        var node = new FunctionCallNode
        {
            TargetGraphId = Guid.NewGuid().ToString(),
            TargetTypeId  = "No.Such.CLR.Type",  // CLR path also fails gracefully
            MethodName    = "Whatever",
            IsPure        = false,
        };

        // Must not throw; falls back to CLR exec-only (type not found).
        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    /// <summary>
    /// FunctionCall with TargetGraphId pointing to an Event graph (not Function):
    /// also falls through to CLR-reflection path (guard: g.Kind == GraphKind.Function).
    /// </summary>
    [Fact]
    public void FunctionCall_TargetGraphId_EventGraph_NotFunction_FallsBackToCLRPath()
    {
        var targetGraphId = Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            Graphs = new List<Graph>
            {
                new Graph { Id = targetGraphId, Kind = GraphKind.Event }, // Event, not Function
            },
        };
        var node = new FunctionCallNode
        {
            TargetGraphId = targetGraphId.ToString(),
            TargetTypeId  = "No.Such.CLR.Type",
            IsPure        = false,
        };

        var pins = NodePinSchema.GetCanonicalPins(node, asset: asset);

        // CLR path: exec-only (type not found).
        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    // ── BATCH-03C: Compiler-selector round-trip check ───────────────────────────

    /// <summary>
    /// Round-trip verification: projected Entry/Return/FunctionCall pins satisfy the exact
    /// compiler selectors from Stage5_Schedule.cs.
    /// - Entry: data-OUT pins (Stage5~1162: <c>!IsExec &amp;&amp; Direction=="Out"</c>) are present and named.
    /// - Return: value pin has Direction=="Out" (Stage5~891: <c>!IsExec &amp;&amp; Direction=="Out"</c>).
    /// - FunctionCall data-IN pins are present for args (Stage5~661: ResolveAllDataInputs reads
    ///   all !IsExec &amp;&amp; Direction=="In" pins positionally); first data-OUT is the return slot.
    /// </summary>
    [Fact]
    public void CompilerSelectors_EntryReturnFunctionCall_AllProjectedPinsSatisfySelectors()
    {
        var targetGraphId = Guid.NewGuid();
        var graph = new Graph
        {
            Id   = targetGraphId,
            Kind = GraphKind.Function,
            Inputs = new List<ParameterDecl>
            {
                new ParameterDecl { Name = "X", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
            Outputs = new List<ParameterDecl>
            {
                new ParameterDecl { Name = "Out", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
        };
        var asset = new BlueprintAsset { Graphs = new List<Graph> { graph } };

        // ── Entry node (data-OUT pins → IrOp_ReadInputArg) ──
        var entryPins = NodePinSchema.GetCanonicalPins(new EventEntryNode(), containingGraph: graph);
        var entryDataOut = entryPins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
        Assert.Single(entryDataOut); // one input → one data-Out
        Assert.Equal("X", entryDataOut[0].Name);

        // ── Return node (first !IsExec && Direction=="Out" → return value) ──
        var returnPins = NodePinSchema.GetCanonicalPins(new ReturnNode(), containingGraph: graph);
        var returnValPin = returnPins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
        Assert.NotNull(returnValPin);  // compiler requires this
        Assert.Equal("Out", returnValPin!.Name);

        // ── FunctionCall node (data-IN positional args + first data-OUT return slot) ──
        var callNode = new FunctionCallNode { TargetGraphId = targetGraphId.ToString(), IsPure = false };
        var callPins = NodePinSchema.GetCanonicalPins(callNode, asset: asset);
        var callDataIn  = callPins.Where(p => !p.IsExec && p.Direction == "In").ToList();
        var callDataOut = callPins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
        Assert.Single(callDataIn);   // one input arg
        Assert.Single(callDataOut);  // one return slot
        Assert.Equal("X",   callDataIn[0].Name);   // readable name matches input
        Assert.Equal("Out", callDataOut[0].Name);  // readable name matches output
    }

    // ── BATCH-03C2: CallPeerBlueprintNode with peerSignatureLookup ──────────────

    /// <summary>
    /// Builds a stub <see cref="BlueprintSignature"/> for a peer with one Function graph
    /// that has 2 typed inputs + 1 typed output.
    /// </summary>
    private static BlueprintSignature MakePeerSig(Guid assetId, string funcName,
        (string Name, string TypeId)[] inputs, (string Name, string TypeId)[] outputs)
    {
        var funcSig = new BlueprintFunctionSig(
            funcName,
            inputs.Select(x  => new BlueprintParamSig(x.Name, x.TypeId)).ToArray(),
            outputs.Select(x => new BlueprintParamSig(x.Name, x.TypeId)).ToArray());

        return new BlueprintSignature(
            Path:              "Peer.bp.json",
            AssetId:           assetId,
            Name:              "PeerLib",
            SanitizedName:     "PeerLib",
            BlueprintId:       42,
            Dispatch:          Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            ExportedFunctions: new[] { funcSig },
            Hostings:          Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: Array.Empty<Guid>());
    }

    /// <summary>
    /// CallPeerBlueprint with a stub lookup providing a peer signature with matching
    /// FunctionRef (2 typed inputs + 1 typed output) → exec In/Out + 2 data-IN (names/types/order)
    /// + Return data-OUT typed from the output.
    /// Grounded in Stage5_Schedule.cs:656-673: ResolveAllDataInputs reads all data-IN pins
    /// positionally; first data-OUT is the return slot.
    /// </summary>
    [Fact]
    public void CallPeerBlueprint_WithLookup_MatchingFunctionRef_ProjectsTypedPins()
    {
        var peerId   = Guid.NewGuid();
        var peerSig  = MakePeerSig(
            peerId,
            "Compute",
            inputs:  new[] { ("Threshold", "System.Single"), ("Count", "System.Int32") },
            outputs: new[] { ("Result",    "System.Double") });

        var lookup = (Guid id) => id == peerId ? peerSig : null;
        var node = new CallPeerBlueprintNode
        {
            PeerBlueprintId = peerId.ToString(),
            FunctionRef     = "Compute",
        };

        var pins = NodePinSchema.GetCanonicalPins(node, peerSignatureLookup: lookup);

        // Exec In/Out always present.
        Assert.True(HasExec(pins, "In",  "In"),  "exec In missing");
        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");

        // Two data-IN pins in declaration order (Stage5 ResolveAllDataInputs positional).
        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        Assert.Equal("Threshold",    dataIn[0].Name);
        Assert.Equal("System.Single", dataIn[0].TypeId);
        Assert.Equal("Count",        dataIn[1].Name);
        Assert.Equal("System.Int32",  dataIn[1].TypeId);

        // Single Return data-OUT pin typed from Outputs[0].
        var dataOut = Data(pins, "Out");
        var ret = Assert.Single(dataOut);
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Double", ret.TypeId);
    }

    /// <summary>
    /// CallPeerBlueprint with NO lookup → static fallback: exec In/Out + Return:System.Object,
    /// no data-IN pins.
    /// </summary>
    [Fact]
    public void CallPeerBlueprint_NullLookup_StaticFallback()
    {
        var node = new CallPeerBlueprintNode
        {
            PeerBlueprintId = Guid.NewGuid().ToString(),
            FunctionRef     = "Compute",
        };

        var pins = NodePinSchema.GetCanonicalPins(node, peerSignatureLookup: null);

        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
        Assert.Empty(Data(pins, "In"));

        var ret = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Object", ret.TypeId);
    }

    /// <summary>
    /// CallPeerBlueprint with a lookup that returns null for the peer GUID
    /// → static fallback (exec In/Out + Return:System.Object).
    /// </summary>
    [Fact]
    public void CallPeerBlueprint_UnknownPeer_StaticFallback()
    {
        var peerId  = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var peerSig = MakePeerSig(
            peerId, "Compute",
            inputs:  new[] { ("X", "System.Int32") },
            outputs: new[] { ("Y", "System.Int32") });

        // Lookup only knows peerId, not otherId.
        var lookup = (Guid id) => id == peerId ? peerSig : null;
        var node = new CallPeerBlueprintNode
        {
            PeerBlueprintId = otherId.ToString(),  // unknown peer
            FunctionRef     = "Compute",
        };

        var pins = NodePinSchema.GetCanonicalPins(node, peerSignatureLookup: lookup);

        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
        Assert.Empty(Data(pins, "In"));
        var ret = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Object", ret.TypeId);
    }

    /// <summary>
    /// CallPeerBlueprint with a lookup that returns a peer signature, but the FunctionRef
    /// does not match any exported function → static fallback.
    /// </summary>
    [Fact]
    public void CallPeerBlueprint_UnknownFunctionRef_StaticFallback()
    {
        var peerId  = Guid.NewGuid();
        var peerSig = MakePeerSig(
            peerId, "Compute",
            inputs:  new[] { ("X", "System.Int32") },
            outputs: new[] { ("Y", "System.Int32") });

        var lookup = (Guid id) => id == peerId ? peerSig : null;
        var node = new CallPeerBlueprintNode
        {
            PeerBlueprintId = peerId.ToString(),
            FunctionRef     = "NoSuchFunction",  // not in peer sig
        };

        var pins = NodePinSchema.GetCanonicalPins(node, peerSignatureLookup: lookup);

        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
        Assert.Empty(Data(pins, "In"));
        var ret = Assert.Single(Data(pins, "Out"));
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Object", ret.TypeId);
    }

    // ── BATCH-03C2: BlueprintSignatureBuilder.FromInMemoryAsset ExportedFunctions ──

    /// <summary>
    /// FromInMemoryAsset on an asset with one Function graph (2 inputs, 1 output) →
    /// ExportedFunctions contains the correct BlueprintFunctionSig with correct names/types;
    /// ExportedFunctionNames (computed property) still returns the function names.
    /// </summary>
    [Fact]
    public void FromInMemoryAsset_FunctionGraph_PopulatesExportedFunctions_AndExportedFunctionNames()
    {
        var assetId = Guid.NewGuid();
        var asset   = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "TestLib",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   = new List<Graph>
            {
                new Graph
                {
                    Id   = Guid.NewGuid(),
                    Name = "Compute",
                    Kind = GraphKind.Function,
                    Inputs = new List<ParameterDecl>
                    {
                        new ParameterDecl { Name = "Value",  Type = new BlueprintTypeRef { TypeId = "System.Single" } },
                        new ParameterDecl { Name = "Factor", Type = new BlueprintTypeRef { TypeId = "System.Int32"  } },
                    },
                    Outputs = new List<ParameterDecl>
                    {
                        new ParameterDecl { Name = "Result", Type = new BlueprintTypeRef { TypeId = "System.Double" } },
                    },
                },
            },
        };

        var sig = BlueprintSignatureBuilder.FromInMemoryAsset(asset);

        // ExportedFunctions populated.
        Assert.Single(sig.ExportedFunctions);
        var func = sig.ExportedFunctions[0];
        Assert.Equal("Compute", func.Name);

        // Inputs.
        Assert.Equal(2, func.Inputs.Count);
        Assert.Equal("Value",         func.Inputs[0].Name);
        Assert.Equal("System.Single", func.Inputs[0].TypeId);
        Assert.Equal("Factor",        func.Inputs[1].Name);
        Assert.Equal("System.Int32",  func.Inputs[1].TypeId);

        // Outputs.
        Assert.Single(func.Outputs);
        Assert.Equal("Result",        func.Outputs[0].Name);
        Assert.Equal("System.Double", func.Outputs[0].TypeId);

        // ExportedFunctionNames computed property still works (backward compat).
        Assert.Contains("Compute", sig.ExportedFunctionNames);
        Assert.Single(sig.ExportedFunctionNames);
    }

    /// <summary>
    /// FromInMemoryAsset with zero Function graphs → ExportedFunctions empty,
    /// ExportedFunctionNames empty.
    /// </summary>
    [Fact]
    public void FromInMemoryAsset_NoFunctionGraphs_ExportedFunctionsEmpty()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "EmptyLib",
            Dispatch = BlueprintDispatchKind.Library,
        };

        var sig = BlueprintSignatureBuilder.FromInMemoryAsset(asset);

        Assert.Empty(sig.ExportedFunctions);
        Assert.Empty(sig.ExportedFunctionNames);
    }

    // ── BATCH-03C2: BlueprintSignatureParser round-trip ──────────────────────────

    /// <summary>
    /// Parser round-trip: a .bp.json snippet with a Function graph that has inputs + outputs
    /// → ExportedFunctions populated with correct names/types, agreeing with FromInMemoryAsset.
    /// Verifies the parser uses lowercase JSON property names ("inputs", "outputs", "name", "type",
    /// "typeid") consistent with the Stage8 test fixture convention.
    /// </summary>
    [Fact]
    public void BlueprintSignatureParser_FunctionGraphWithParams_ParsesExportedFunctions()
    {
        var json = """
        {
            "assetId": "aaaabbbb-cccc-dddd-eeee-ffffffffffff",
            "name": "TestLib",
            "dispatch": "Library",
            "graphs": [
                {
                    "id": "g1",
                    "name": "Compute",
                    "kind": "Function",
                    "inputs": [
                        { "name": "Value",  "type": { "typeid": "System.Single" } },
                        { "name": "Factor", "type": { "typeid": "System.Int32"  } }
                    ],
                    "outputs": [
                        { "name": "Result", "type": { "typeid": "System.Double" } }
                    ],
                    "nodes": [],
                    "links": []
                }
            ]
        }
        """;

        var sig = Hrot.Blueprints.Core.Compiler.BlueprintSignatureParser.Parse("test.bp.json", json);

        Assert.Single(sig.ExportedFunctions);
        var func = sig.ExportedFunctions[0];
        Assert.Equal("Compute", func.Name);

        Assert.Equal(2, func.Inputs.Count);
        Assert.Equal("Value",         func.Inputs[0].Name);
        Assert.Equal("System.Single", func.Inputs[0].TypeId);
        Assert.Equal("Factor",        func.Inputs[1].Name);
        Assert.Equal("System.Int32",  func.Inputs[1].TypeId);

        Assert.Single(func.Outputs);
        Assert.Equal("Result",        func.Outputs[0].Name);
        Assert.Equal("System.Double", func.Outputs[0].TypeId);

        // ExportedFunctionNames backward-compat.
        Assert.Contains("Compute", sig.ExportedFunctionNames);
    }

    /// <summary>
    /// Parser round-trip with missing "inputs"/"outputs" arrays → empty lists (graceful).
    /// </summary>
    [Fact]
    public void BlueprintSignatureParser_FunctionGraphMissingInputsOutputs_EmptyLists()
    {
        var json = """
        {
            "assetId": "aaaabbbb-cccc-dddd-eeee-000000000001",
            "name": "TestLib",
            "dispatch": "Library",
            "graphs": [
                {
                    "id": "g1",
                    "name": "Execute",
                    "kind": "Function",
                    "nodes": [],
                    "links": []
                }
            ]
        }
        """;

        var sig = Hrot.Blueprints.Core.Compiler.BlueprintSignatureParser.Parse("test.bp.json", json);

        Assert.Single(sig.ExportedFunctions);
        var func = sig.ExportedFunctions[0];
        Assert.Equal("Execute", func.Name);
        Assert.Empty(func.Inputs);
        Assert.Empty(func.Outputs);
    }

    // ── BP-4: Single-source invariant (NodePinSchema delegates to BuiltInNodeRegistry) ──────────

    /// <summary>
    /// BP-4 single-source invariant: for a representative static node (BranchNode),
    /// the editor canonical pins produced by <see cref="NodePinSchema.GetCanonicalPins"/>
    /// exactly match the compiler registry shapes from
    /// <see cref="BuiltInNodeRegistry.Instance.GetStaticPins"/> in count, name, direction,
    /// IsExec and TypeId — and in the same order (order is load-bearing for link-GUID assignment).
    ///
    /// This test locks the refactoring invariant: there is one source of truth for static pin
    /// shapes and the editor will never silently drift from it.
    /// </summary>
    [Fact]
    public void StaticNode_EditorPins_ExactlyMatchRegistryShapes_InOrder()
    {
        // Use BranchNode as the representative static node: it has a rich set of pins
        // (exec In + exec True/Out + exec False/Out + data-In Condition) that cover all
        // Name, Direction, IsExec, and TypeId fields.
        var node     = new BranchNode();
        var registry = BuiltInNodeRegistry.Instance;

        // Editor canonical pins (via the refactored NodePinSchema delegation path).
        var editorPins   = NodePinSchema.GetCanonicalPins(node);

        // Compiler registry shapes (the single source of truth post-BP-4).
        var registryPins = registry.GetStaticPins(node);

        // Same count.
        Assert.Equal(registryPins.Count, editorPins.Count);

        // Same shape in the same order (order is load-bearing for BlueprintGraphModel's
        // positional link-GUID assignment).
        for (var i = 0; i < registryPins.Count; i++)
        {
            var r = registryPins[i];
            var e = editorPins[i];
            Assert.Equal(r.Name,      e.Name);
            Assert.Equal(r.Direction, e.Direction);
            Assert.Equal(r.IsExec,    e.IsExec);
            Assert.Equal(r.TypeId,    e.TypeRef?.TypeId ?? "");
        }
    }

    /// <summary>
    /// BP-4 single-source invariant: ScoreDecisionNode (exec In/Out + WinningOptionId data-OUT)
    /// verifies the data pin TypeId (System.Byte) also matches between editor and registry.
    /// </summary>
    [Fact]
    public void StaticNode_ScoreDecision_EditorPins_ExactlyMatchRegistryShapes_InOrder()
    {
        var node         = new ScoreDecisionNode();
        var registryPins = BuiltInNodeRegistry.Instance.GetStaticPins(node);
        var editorPins   = NodePinSchema.GetCanonicalPins(node);

        Assert.Equal(registryPins.Count, editorPins.Count);
        for (var i = 0; i < registryPins.Count; i++)
        {
            var r = registryPins[i];
            var e = editorPins[i];
            Assert.Equal(r.Name,      e.Name);
            Assert.Equal(r.Direction, e.Direction);
            Assert.Equal(r.IsExec,    e.IsExec);
            Assert.Equal(r.TypeId,    e.TypeRef?.TypeId ?? "");
        }
    }

    // ── AN7: non-channel action pin projection ──────────────────────────────────

    /// <summary>
    /// Minimal stub <see cref="IBehaviorActionCatalog"/> for AN7 pin tests.
    /// </summary>
    private sealed class StubBehaviorActionCatalog : IBehaviorActionCatalog
    {
        private readonly IReadOnlyList<BehaviorActionEntry> _entries;

        public StubBehaviorActionCatalog(params BehaviorActionEntry[] entries)
            => _entries = entries;

        public IReadOnlyList<BehaviorActionEntry> GetActions() => _entries;

        public IReadOnlyList<BehaviorActionEntry> GetActions(BehaviorActionHosts host)
        {
            var result = new List<BehaviorActionEntry>();
            foreach (var e in _entries)
                if ((e.ValidHosts & host) != 0)
                    result.Add(e);
            return result;
        }

        public event Action? Changed { add { } remove { } }
    }

    /// <summary>Multi-field DTO for AN7 non-channel pin reflection tests.</summary>
    public static class NonChannelReflectionTargets
    {
        /// <summary>A blittable params DTO with a primitive + an enum field.</summary>
        public struct FakeSharedParams
        {
            public int    Intensity;
            public DemoGait Gait;
        }

        /// <summary>Enum with two members — stamp "global::" per AN6.</summary>
        public enum DemoGait { Walk, Run }
    }

    private static BehaviorActionEntry MakeSharedEntry(string fqn, string paramsFqn) =>
        new BehaviorActionEntry(
            Id:             fqn,
            DisplayName:    "DoThing",
            Category:       "FakeActions",
            ChannelTypeFqn: null,
            ActionId:       0,
            ParamsTypeFqn:  paramsFqn,
            ValidHosts:     BehaviorActionHosts.Blueprint | BehaviorActionHosts.BTree | BehaviorActionHosts.Hsm,
            Source:         BehaviorActionSource.Hardcoded);

    /// <summary>
    /// AN7: ChannelCommandNode with ActionFqn set and a catalog that resolves it → exec In/Out +
    /// data-IN pins from the DTO's public fields (Intensity:int + Gait stamped "global::").
    /// </summary>
    [Fact]
    public void NonChannelAction_KnownFqn_MultiFieldParams_ProjectsOnePinPerField_PlusExec()
    {
        var fqn = "Fake.Ns.FakeActions.DoThing";
        var paramsFqn = typeof(NonChannelReflectionTargets.FakeSharedParams).FullName!;
        var catalog = new StubBehaviorActionCatalog(MakeSharedEntry(fqn, paramsFqn));
        var node = new ChannelCommandNode { ActionFqn = fqn };

        var pins = NodePinSchema.GetCanonicalPins(node, behaviorActions: catalog);

        Assert.True(HasExec(pins, "In",  "In"),  "exec In missing");
        Assert.True(HasExec(pins, "Out", "Out"), "exec Out missing");

        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        Assert.Contains(dataIn, p => p.Name == "Intensity" && p.TypeId == "System.Int32");
        // Enum field must have "global::" prefix per AN6.
        var gaitPinTypeExpected = "global::" + typeof(NonChannelReflectionTargets.DemoGait).FullName;
        Assert.Contains(dataIn, p => p.Name == "Gait" && p.TypeId == gaitPinTypeExpected);
        // No data-OUT pins.
        Assert.Empty(Data(pins, "Out"));
    }

    /// <summary>
    /// AN7: ChannelCommandNode with ActionFqn set but null catalog → exec-only (graceful fallback).
    /// </summary>
    [Fact]
    public void NonChannelAction_NullCatalog_ExecOnly()
    {
        var node = new ChannelCommandNode { ActionFqn = "Some.Ns.Actions.DoThing" };

        var pins = NodePinSchema.GetCanonicalPins(node, behaviorActions: null);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    /// <summary>
    /// AN7: ChannelCommandNode with ActionFqn not in the catalog → exec-only (graceful fallback).
    /// </summary>
    [Fact]
    public void NonChannelAction_UnknownFqn_ExecOnly()
    {
        var catalog = new StubBehaviorActionCatalog(
            MakeSharedEntry("Known.Ns.Actions.KnownAction", typeof(ReflectionTargets.FakeFireParams).FullName!));
        var node = new ChannelCommandNode { ActionFqn = "Unknown.Ns.NoSuchAction" };

        var pins = NodePinSchema.GetCanonicalPins(node, behaviorActions: catalog);

        Assert.Empty(Data(pins, "In"));
        Assert.Empty(Data(pins, "Out"));
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
    }

    /// <summary>
    /// AN7: ChannelCommandNode with ActionFqn=null falls through to the channel-command path
    /// (existing behavior, byte-stable — no regression).
    /// </summary>
    [Fact]
    public void NonChannelAction_NullActionFqn_FallsThroughToChannelCommandPath()
    {
        var fqn = typeof(ReflectionTargets.FakeFireParams).FullName!;
        var channelCatalog = new StubChannelCommandCatalog(
            new ChannelCommandCatalogEntry("Fire", "Some.Ns.WeaponChannel", 1, fqn));
        var node = new ChannelCommandNode
        {
            ActionFqn   = null,       // null → channel-command path
            ChannelType = "WeaponChannel",
            ActionId    = "Fire",
        };

        var pins = NodePinSchema.GetCanonicalPins(node, channelCommands: channelCatalog);

        // Channel-command path: exec + 2 data-IN from FakeFireParams.
        Assert.True(HasExec(pins, "In",  "In"));
        Assert.True(HasExec(pins, "Out", "Out"));
        var dataIn = Data(pins, "In");
        Assert.Equal(2, dataIn.Length);
        Assert.Contains(dataIn, p => p.Name == "Target");
        Assert.Contains(dataIn, p => p.Name == "CooldownSeconds");
    }

    /// <summary>
    /// AN7: JSON round-trip — ActionFqn is omitted when null (byte-stable for existing
    /// channel-command assets) and present when set.
    /// </summary>
    [Fact]
    public void ChannelCommandNode_ActionFqn_JsonRoundTrip_OmittedWhenNull_PresentWhenSet()
    {
        // ── Null ActionFqn → property absent from JSON ──
        var channelNode = new ChannelCommandNode { ChannelType = "WeaponChannel", ActionId = "Fire" };
        var json = System.Text.Json.JsonSerializer.Serialize(channelNode,
            new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        Assert.DoesNotContain("ActionFqn", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actionFqn", json, StringComparison.OrdinalIgnoreCase);

        // ── Set ActionFqn → present in JSON, channel fields empty ──
        var nonChannelNode = new ChannelCommandNode { ActionFqn = "Foo.Bar.Actions.DoThing" };
        var json2 = System.Text.Json.JsonSerializer.Serialize(nonChannelNode);
        Assert.Contains("ActionFqn", json2);
        Assert.Contains("Foo.Bar.Actions.DoThing", json2);

        // ── Round-trip: deserialize back preserves ActionFqn ──
        // Use the full BlueprintJsonServices path so polymorphic dispatch works.
        var assetJson = $$"""
        {
            "assetId": "aaaabbbb-0000-0000-0000-000000000001",
            "name": "RoundTripTest",
            "dispatch": "Instance",
            "graphs": [{
                "id": "00000000-0000-0000-0000-000000000001",
                "name": "Tick",
                "kind": "Event",
                "nodes": [{
                    "kind": "ChannelCommand",
                    "id":   "00000000-0000-0000-0000-000000000002",
                    "ActionFqn": "Foo.Bar.Actions.DoThing",
                    "ChannelType": "",
                    "ActionId": "",
                    "Pins": [],
                    "EditorMetadata": {}
                }],
                "links": []
            }]
        }
        """;
        var asset = Hrot.Blueprints.Core.BlueprintJsonServices.Deserialize(assetJson);
        var node  = Assert.Single(asset!.Graphs[0].Nodes);
        var cc    = Assert.IsType<ChannelCommandNode>(node);
        Assert.Equal("Foo.Bar.Actions.DoThing", cc.ActionFqn);
        Assert.Equal("", cc.ChannelType);
        Assert.Equal("", cc.ActionId);
    }
}

/// <summary>Local helper struct used by the enrichment tests to capture pin shape.</summary>
internal static class IPinAssertHelpers
{
    internal readonly record struct PinView(string Name, string Direction, string TypeId);
}

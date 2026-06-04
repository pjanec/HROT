using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.Host;
using Xunit;

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
}

/// <summary>Local helper struct used by the enrichment tests to capture pin shape.</summary>
internal static class IPinAssertHelpers
{
    internal readonly record struct PinView(string Name, string Direction, string TypeId);
}

using Fdp.Toolkit.Behavior.Demo;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler.EndToEnd;

/// <summary>
/// AN8 end-to-end tests for non-channel behavior-action invocation (INLINE-LATENT model).
/// Verifies that a <see cref="ChannelCommandNode"/> with <c>ActionFqn</c> set is correctly
/// compiled through all stages: Schedule → Lower → Emit (Stage 7 generated source).
/// </summary>
public sealed class InlineAction_EndToEndTests
{
    // Synthetic FQN that follows the AiPrimitive BlueprintCall convention:
    // "{Namespace}.{SanitizedName}_{BlueprintId:X8}_Bp.Call"
    private const string FakeActionFqn = "Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.Call";
    private const string FakeParamsFqn = "Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp+Params";

    // Derived class FQN (ActionFqn without trailing ".Call")
    private const string FakeClassFqn  = "Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp";

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>
    /// Runs Stages 2–7 on the given asset and returns the generated C# source.
    /// Throws if any diagnostic error is produced.
    /// </summary>
    private static string EmitAndGetSource(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        Stage2_Validate.Run(asset, ctx);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(lowered, CompilerMode.Debug, sink);

        if (sink.HasErrors)
            throw new InvalidOperationException(
                $"Pipeline errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        return src;
    }

    /// <summary>
    /// Runs only Stages 3–5 (no validation, no emit) to inspect the scheduled IR.
    /// Used for assertions on IrOp presence before lowering.
    /// </summary>
    private static IrAsset RunSchedule(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        return Stage5_Schedule.Run(typed, ctx);
    }

    // =========================================================================
    // Stage-5 scheduling tests
    // =========================================================================

    [Fact]
    public void ActionInvocation_AiPrimitive_SchedulesInlineActionCallOp()
    {
        // A ChannelCommandNode with ActionFqn set should produce IrOp_InlineActionCall.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var ir = RunSchedule(asset);

        var allOps = ir.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Select(s => s.Operation);

        Assert.Contains(allOps, op => op is IrOp_InlineActionCall iac
            && iac.ActionFqn == FakeActionFqn
            && iac.IsAiPrimitive);
    }

    [Fact]
    public void ActionInvocation_Instance_SchedulesInlineActionCallOp()
    {
        var asset = BlueprintAssetBuilder
            .Instance("MoveInstance")
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var ir = RunSchedule(asset);

        var allOps = ir.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Select(s => s.Operation);

        Assert.Contains(allOps, op => op is IrOp_InlineActionCall iac
            && iac.ActionFqn == FakeActionFqn);
    }

    // =========================================================================
    // Stage-6 lowering tests
    // =========================================================================

    [Fact]
    public void ActionInvocation_AiPrimitive_NoSuspendAfterLowering()
    {
        // After Stage 6 (WaitLowering_AiPrimitive), IrTerm_Suspend must be gone.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var sink = new DiagnosticSink();
        var opts = DefaultOptions();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(lowered.Graphs);
        Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);
    }

    [Fact]
    public void ActionInvocation_Instance_NoSuspendAfterLowering()
    {
        // After Stage 6 (WaitLowering_Instance), IrTerm_Suspend must be gone.
        var asset = BlueprintAssetBuilder
            .Instance("MoveInstance")
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var sink = new DiagnosticSink();
        var opts = DefaultOptions();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(lowered.Graphs);
        Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);
    }

    [Fact]
    public void ActionInvocation_AiPrimitive_LoweringProducesPhaseDispatchEntry()
    {
        // AiPrimitive with inline-latent should have a dispatch entry block.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var sink = new DiagnosticSink();
        var opts = DefaultOptions();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(lowered.Graphs);
        var entryBlock = graph.Blocks.First(b => b.Id == graph.Entry);
        Assert.Equal("dispatch", entryBlock.Label);
    }

    // =========================================================================
    // Stage-7 emit tests (generated C# source content)
    // =========================================================================

    [Fact]
    public void ActionInvocation_AiPrimitive_EmittedSource_ContainsActionCall()
    {
        // The generated source must invoke the action class's Call method.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // Should reference the action class and Call method.
        Assert.Contains(FakeClassFqn, src);
        Assert.Contains(".Call(", src);
    }

    [Fact]
    public void ActionInvocation_AiPrimitive_EmittedSource_ContainsBlackboard1024Projection()
    {
        // Must project Blackboard1024 and reference StructureHash.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("Blackboard1024", src);
        Assert.Contains("StructureHash", src);
    }

    [Fact]
    public void ActionInvocation_AiPrimitive_EmittedSource_ContainsWorkingStateRef()
    {
        // Must project WorkingState from Blackboard1024 memory (unsafe pointer arithmetic).
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("__ws_", src);
        Assert.Contains("WorkingState", src);
    }

    [Fact]
    public void ActionInvocation_AiPrimitive_EmittedSource_ContainsNodeStatusRouting()
    {
        // The inline-latent model must route on NodeStatus (Success/Failure vs Running).
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // Running → suspend path must be present.
        Assert.Contains("NodeStatus.Running", src);
        // Failure routing (WriteWorkingStatePhase or return Failure) must be present.
        Assert.Contains("NodeStatus.Failure", src);
    }

    [Fact]
    public void ActionInvocation_Instance_EmittedSource_ContainsActionCall()
    {
        // Instance blueprint path: cursor-based latent suspend.
        var asset = BlueprintAssetBuilder
            .Instance("MoveInstance")
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains(FakeClassFqn, src);
        Assert.Contains(".Call(", src);
    }

    [Fact]
    public void ActionInvocation_Instance_EmittedSource_ContainsCursorResumeAt()
    {
        // Instance inline-latent must write a cursor resume-at slot for the suspend point.
        var asset = BlueprintAssetBuilder
            .Instance("MoveInstance")
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("ResumeAt", src);
    }

    [Fact]
    public void ActionInvocation_AiPrimitive_EmittedSource_ContainsUnsafeBlock()
    {
        // Blackboard1024 projection must be inside an unsafe block.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("unsafe", src);
        Assert.Contains("fixed (byte*", src);
    }

    // =========================================================================
    // Regression: existing channel-command path unaffected
    // =========================================================================

    [Fact]
    public void ChannelCommand_WithNullActionFqn_IsUnaffectedByAN8()
    {
        // Existing ChannelCommandNode (ActionFqn null) must still compile.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ChannelCommand("LocomotionChannel", "MoveTo")
                .WaitForChannel("LocomotionChannel")
                .Return())
            .Build();

        var sink = new DiagnosticSink();
        var opts = DefaultOptions();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Regression: channel-command path failed: {string.Join(", ", sink.All.Select(d => d.Code))}");

        // Should NOT produce IrOp_InlineActionCall — that is for ActionFqn-set nodes only.
        var allOps = lowered.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Select(s => s.Operation);

        Assert.DoesNotContain(allOps, op => op is IrOp_InlineActionCall);
    }
}

/// <summary>
/// AN8b end-to-end compiler tests for <c>[SharedAiAction]</c> direct-invocation lowering.
/// Verifies that a <see cref="ChannelCommandNode"/> with a non-AiPrimitive <c>ActionFqn</c>
/// (the <see cref="DemoSharedActions.AlertNearbyUnits"/> demo method) compiles through all
/// stages without errors and that the generated C# source contains the expected direct call.
/// </summary>
public sealed class SharedAiAction_EndToEndTests
{
    // The demo [SharedAiAction] FQN: "{DeclaringType.FullName}.{MethodName}"
    // Does NOT end with "_Bp.Call" → IsAiPrimitive == false.
    private static readonly string DemoActionFqn =
        $"{typeof(DemoSharedActions).FullName}.{nameof(DemoSharedActions.AlertNearbyUnits)}";

    private static readonly string DemoParamsFqn =
        typeof(DemoSharedActionParams).FullName!;

    // The declaring type portion of the FQN (used in assertions).
    private static readonly string DemoDeclTypeFqn =
        typeof(DemoSharedActions).FullName!;

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static string EmitAndGetSource(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        Stage2_Validate.Run(asset, ctx);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(lowered, CompilerMode.Debug, sink);

        if (sink.HasErrors)
            throw new InvalidOperationException(
                $"Pipeline errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        return src;
    }

    private static IrAsset RunSchedule(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        return Stage5_Schedule.Run(typed, ctx);
    }

    // =========================================================================
    // IsAiPrimitive discriminator
    // =========================================================================

    [Fact]
    public void SharedAiAction_Stage5_SchedulesInlineActionCall_IsAiPrimitiveFalse_AN8b()
    {
        // A SharedAiAction FQN (not ending in "_Bp.Call") must produce IsAiPrimitive==false.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var ir = RunSchedule(asset);

        var allOps = ir.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Select(s => s.Operation);

        Assert.Contains(allOps, op => op is IrOp_InlineActionCall iac
            && iac.ActionFqn == DemoActionFqn
            && !iac.IsAiPrimitive);
    }

    [Fact]
    public void AiPrimitiveFqn_Stage5_SchedulesInlineActionCall_IsAiPrimitiveTrue_AN8b()
    {
        // Regression: a genuine AiPrimitive FQN (ends with "_Bp.Call") must still produce IsAiPrimitive==true.
        const string fakeAiFqn    = "Hrot.AI.Behaviors.Generated.SomeAction_DEADBEEF_Bp.Call";
        const string fakeParamsFqn = "Hrot.AI.Behaviors.Generated.SomeAction_DEADBEEF_Bp+Params";

        var asset = BlueprintAssetBuilder
            .AiPrimitive("AiPrimTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(fakeAiFqn, fakeParamsFqn)
                .Return())
            .Build();

        var ir = RunSchedule(asset);

        var allOps = ir.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Select(s => s.Operation);

        Assert.Contains(allOps, op => op is IrOp_InlineActionCall iac
            && iac.ActionFqn == fakeAiFqn
            && iac.IsAiPrimitive);
    }

    // =========================================================================
    // Stage-6 lowering: latent suspend/resume machinery is reused
    // =========================================================================

    [Fact]
    public void SharedAiAction_AiPrimitive_NoSuspendAfterLowering_AN8b()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var sink = new DiagnosticSink();
        var opts = DefaultOptions();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(lowered.Graphs);
        Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);
    }

    [Fact]
    public void SharedAiAction_Instance_NoSuspendAfterLowering_AN8b()
    {
        var asset = BlueprintAssetBuilder
            .Instance("SharedActionInstance")
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var sink = new DiagnosticSink();
        var opts = DefaultOptions();
        var ctx  = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(lowered.Graphs);
        Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);
    }

    // =========================================================================
    // Stage-7 emit: generated source content
    // =========================================================================

    [Fact]
    public void SharedAiAction_AiPrimitive_CompileSucceeds_NoDiagnosticsNoHashError_AN8b()
    {
        // Primary AN8b contract: BlueprintCompiler.Compile SUCCEEDS with no #error and no diagnostics.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        // EmitAndGetSource throws if any diagnostic error is produced.
        var src = EmitAndGetSource(asset);

        // Source must not contain the old #error sentinel.
        Assert.DoesNotContain("#error AN8", src, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedAiAction_AiPrimitive_EmittedSource_ContainsDirectMethodCall_AN8b()
    {
        // The generated source must call the method directly (not via ".Call").
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // Should reference the declaring type AND the method name via global:: prefix.
        Assert.Contains($"global::{DemoActionFqn}(", src);
    }

    [Fact]
    public void SharedAiAction_AiPrimitive_EmittedSource_ContainsParamsDto_AN8b()
    {
        // The generated source must build the params DTO from pins.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // Params DTO type must appear (with '+' normalised to '.').
        Assert.Contains(DemoParamsFqn.Replace('+', '.'), src);
    }

    [Fact]
    public void SharedAiAction_AiPrimitive_EmittedSource_ContainsNodeStatusRouting_AN8b()
    {
        // Inline-latent model: Running → suspend, Success/Failure route exec-out.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("NodeStatus.Running",  src);
        Assert.Contains("NodeStatus.Failure",  src);
    }

    [Fact]
    public void SharedAiAction_AiPrimitive_EmittedSource_NoWorkingStateProjectionAtCallSite_AN8b()
    {
        // SharedAiAction must NOT project a per-action WorkingState (no __ws_ local at the call site,
        // no StructureHash check for the called action).
        // Note: the HOST AiPrimitive blueprint's BTree/Hsm thunks DO emit Blackboard1024 + StructureHash
        // for the host class itself — those must not be confused with the SharedAiAction call site.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // The direct call must appear WITHOUT a "ref __ws_" argument (no working-state for SharedAiAction).
        // AiPrimitive call pattern: ClassFqn.Call(ref __p_N, ref __ws_N, self, world, time)
        // SharedAiAction call pattern: global::FQN(ref __p_N, self, world)
        // Assert the SharedAiAction call site has no "ref __ws_" immediately before "self".
        Assert.DoesNotContain($", ref __ws_", src);
    }

    [Fact]
    public void SharedAiAction_AiPrimitive_EmittedSource_ContainsPhaseField_AN8b()
    {
        // The inline-latent machinery (phase dispatch) must be present even for SharedAiAction.
        // Stage 6 WaitLowering injects IrOp_ReadWorkingStatePhase / IrOp_WriteWorkingStatePhase ops
        // which Stage 7 renders as "ws.__phase = N" and "byte __tN = ws.__phase".
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedActionTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // Phase reads/writes are present ("ws.__phase" appears in the state-machine blocks).
        Assert.Contains("ws.__phase", src);
    }

    [Fact]
    public void SharedAiAction_Instance_CompileSucceeds_AN8b()
    {
        // Instance blueprint path: cursor-based latent suspend.
        var asset = BlueprintAssetBuilder
            .Instance("SharedActionInstance")
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(DemoActionFqn, DemoParamsFqn)
                .Return())
            .Build();

        // Should compile without errors.
        var src = EmitAndGetSource(asset);

        Assert.DoesNotContain("#error AN8", src, StringComparison.Ordinal);
        Assert.Contains($"global::{DemoActionFqn}(", src);
        Assert.Contains("NodeStatus.Running",  src);
        Assert.Contains("ResumeAt", src);
    }
}

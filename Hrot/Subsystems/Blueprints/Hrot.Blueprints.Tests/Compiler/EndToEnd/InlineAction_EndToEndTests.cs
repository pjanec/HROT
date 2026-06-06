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

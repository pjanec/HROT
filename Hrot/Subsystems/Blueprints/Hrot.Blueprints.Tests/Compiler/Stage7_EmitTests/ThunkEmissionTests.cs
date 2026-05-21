using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests that verify AiPrimitiveEmitter emits correct BTree/HSM thunk structures.
/// </summary>
public sealed class ThunkEmissionTests
{
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
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(lowered, CompilerMode.Debug, sink);

        if (sink.HasErrors)
            throw new InvalidOperationException(
                $"Emit errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");
        return src;
    }

    [Fact]
    public void BTreeAction_EmitsBTreeTick_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // BTree action thunk method should be present.
        Assert.Contains("BTreeTick", src);
        // BrainBlackboard parameter should use correct namespace.
        Assert.Contains("global::Fdp.Toolkit.Behavior.Components.BrainBlackboard", src);
        // BehaviorTreeState should use Fbt namespace.
        Assert.Contains("global::Fbt.BehaviorTreeState", src);
    }

    [Fact]
    public void BTreeCondition_EmitsBTreeTick_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("HasTarget")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Success))
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("BTreeEvaluate", src);
        Assert.Contains("global::Fdp.Toolkit.Behavior.Components.BrainBlackboard", src);
    }

    [Fact]
    public void HsmAction_EmitsHsmActivity_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("PatrolAction")
            .WithHostings(AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // HSM activity thunk.
        Assert.Contains("HsmActivity", src);
        // Blackboard1024 should use Fdp.Toolkit.Behavior.Components namespace.
        Assert.Contains("global::Fdp.Toolkit.Behavior.Components.Blackboard1024", src);
        // HsmKernelBridge should use Fdp.Toolkit.Behavior.Systems namespace.
        Assert.Contains("global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge", src);
    }

    [Fact]
    public void HsmGuard_EmitsHsmGuard_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("CanPatrol")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.HsmGuard)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Success))
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("HsmGuard", src);
        Assert.Contains("global::Fdp.Toolkit.Behavior.Components.Blackboard1024", src);
    }

    [Fact]
    public void MultipleHostings_EmitsAllThunks()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MultiHosted")
            .WithHostings(AiPrimitiveHosting.BTreeAction, AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("BTreeTick", src);
        Assert.Contains("HsmActivity", src);
    }
}

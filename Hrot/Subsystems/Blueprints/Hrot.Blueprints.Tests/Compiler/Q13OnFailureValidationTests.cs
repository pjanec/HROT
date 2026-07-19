using System;
using System.Linq;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Q#13-B: a WIRED WaitForChannel "OnFailure" exec chain must terminate in an explicit Return node
/// (architect ruling — no implicit-return fall-off on the failure branch). Enforced by
/// V_GraphStructure (pin-ful graphs — the editor/authoring path).
/// </summary>
public sealed class Q13OnFailureValidationTests
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

    private static DiagnosticSink Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink;
    }

    [Fact]
    [CoversDiagnosticCode("BP1102")]
    public void OnFailure_DeadEndChain_EmitsBP1102()
    {
        // OnFailure → CallCustomEvent with an unwired exec-out (dead end, no Return) ⇒ fall-off.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("Q13FailDangling")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry()
                .WaitForChannelWithFailure("LocomotionChannel", fail => fail.CallCustomEvent("cleanup"))
                .Return(NodeStatus.Success))
            .Build();

        Assert.Contains(Validate(asset).All, d => d.Code == DiagnosticCodes.BP1102);
    }

    [Fact]
    public void OnFailure_TerminatedByReturn_NoBP1102()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("Q13FailOk")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry()
                .WaitForChannelWithFailure("LocomotionChannel", fail => fail.Return(NodeStatus.Failure))
                .Return(NodeStatus.Success))
            .Build();

        Assert.DoesNotContain(Validate(asset).All, d => d.Code == DiagnosticCodes.BP1102);
    }

    [Fact]
    public void OnFailure_Unwired_NoBP1102()
    {
        // No OnFailure pin/wire at all (legacy single-exec-out shape) ⇒ nothing to enforce.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("Q13NoFail")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().WaitForChannel("LocomotionChannel").Return())
            .Build();

        Assert.DoesNotContain(Validate(asset).All, d => d.Code == DiagnosticCodes.BP1102);
    }
}

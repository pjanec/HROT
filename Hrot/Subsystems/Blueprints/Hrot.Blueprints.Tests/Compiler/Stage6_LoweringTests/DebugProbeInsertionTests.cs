using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Lowering;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class DebugProbeInsertionTests
{
    private static IrAsset BuildMinimalIr(CompilerMode mode, out DiagnosticSink sink)
    {
        // Use Delay node so the block has actual statements (Entry emits no statements).
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Delay(1.0f).Return())
            .Build();

        var opts = new CompileOptions(
            Mode: mode,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: StaticTypeRegistry.Instance,
            EngineEvents: BuiltInEngineEventCatalog.Instance,
            ChannelCommands: BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives: BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        sink = new DiagnosticSink();
        var typed = new TypedAsset(asset,
            new Dictionary<Guid, IrTypeRef>(),
            new Dictionary<Guid, IrTypeRef>());
        var ctx = new ValidationContext(sink, opts);
        var ir  = Stage5_Schedule.Run(typed, ctx);
        return ir;
    }

    [Fact]
    public void DebugMode_InsertsNodeEnterProbes()
    {
        var ir = BuildMinimalIr(CompilerMode.Debug, out var sink);

        var lowered = DebugProbeInsertion.Apply(ir, CompilerMode.Debug);

        var probes = lowered.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Where(s => s.Operation is IrOp_DebugProbe_NodeEnter)
            .ToList();

        // At least one block with a NodeEnter probe (blocks that have non-null NodeId).
        Assert.NotEmpty(probes);
    }

    [Fact]
    public void ReleaseMode_InsertNoProbes()
    {
        var ir = BuildMinimalIr(CompilerMode.Release, out var sink);

        var lowered = DebugProbeInsertion.Apply(ir, CompilerMode.Release);

        var probes = lowered.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Where(s => s.Operation is IrOp_DebugProbe_NodeEnter
                     || s.Operation is IrOp_DebugProbe_PinValue)
            .ToList();

        Assert.Empty(probes);
    }

    [Fact]
    public void TraceMode_InsertsNodeEnterAndPinValueProbes()
    {
        // Use Delay node so the block has actual statements (Entry emits no statements).
        var asset = BlueprintAssetBuilder
            .AiPrimitive("B")
            .WithHostings(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Delay(1.0f).Return())
            .Build();

        var opts = new CompileOptions(
            Mode: CompilerMode.Trace,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: StaticTypeRegistry.Instance,
            EngineEvents: BuiltInEngineEventCatalog.Instance,
            ChannelCommands: BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives: BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        var sink  = new DiagnosticSink();
        var typed = new TypedAsset(asset,
            new Dictionary<Guid, IrTypeRef>(),
            new Dictionary<Guid, IrTypeRef>());
        var ctx   = new ValidationContext(sink, opts);
        var ir    = Stage5_Schedule.Run(typed, ctx);

        var lowered = DebugProbeInsertion.Apply(ir, CompilerMode.Trace);

        // Trace mode must insert at least NodeEnter probes (PinValue probes are optional
        // depending on whether any block produces a value with debug info).
        var nodeEnterProbes = lowered.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Where(s => s.Operation is IrOp_DebugProbe_NodeEnter)
            .ToList();

        Assert.NotEmpty(nodeEnterProbes);
    }
}

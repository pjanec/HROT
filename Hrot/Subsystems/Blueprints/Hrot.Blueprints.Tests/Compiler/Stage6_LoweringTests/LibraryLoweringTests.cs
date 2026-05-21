using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class LibraryLoweringTests
{
    private static IrAsset BuildAndScheduleLibrary(
        Hrot.Blueprints.Core.Assets.BlueprintAsset asset, DiagnosticSink sink)
    {
        var opts  = new CompileOptions(
            Mode: CompilerMode.Debug,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: StaticTypeRegistry.Instance,
            EngineEvents: BuiltInEngineEventCatalog.Instance,
            ChannelCommands: BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives: BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ctx = new ValidationContext(sink, opts);
        return Stage5_Schedule.Run(typed, ctx);
    }

    [Fact]
    [CoversDiagnosticCode("BP5001")]
    public void Library_WithNoFunctionGraphs_EmitsBP5001()
    {
        // A Library asset that produces no function IrGraphs.
        var asset = BlueprintAssetBuilder
            .Library("EmptyLib")
            .Build(); // No graphs added.

        var sink = new DiagnosticSink();
        var ir   = BuildAndScheduleLibrary(asset, sink);
        Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP5001);
    }

    [Fact]
    [CoversDiagnosticCode("BP9001")]
    public void Library_WithLatentOp_EmitsBP9001()
    {
        // Manually inject a latent operation into the IR (bypasses Stage 2 validation).
        var asset  = BlueprintAssetBuilder
            .Library("LatentLib")
            .WithGraph("G", g => g.Entry().Return())
            .Build();

        var sink = new DiagnosticSink();
        var ir   = BuildAndScheduleLibrary(asset, sink);

        // Inject a fake latent statement into the first block.
        var graphList = ir.Graphs.ToList();
        var graph     = graphList[0];
        var block     = graph.Blocks[0];

        // Synthesize a statement with a latent op.
        var latentStmt = new IrStatement
        {
            ResultValue = null,
            Operation   = new IrOp_LatentDelay(new IrValue(0, new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 })),
            Debug       = new IrDebugAnnotation { GraphId = graph.Id },
        };
        var newStatements = block.Statements.Concat(new[] { latentStmt }).ToList();
        var patchedBlock  = block with { Statements = newStatements };
        var patchedBlocks = graph.Blocks
            .Select(b => b.Id == block.Id ? patchedBlock : b)
            .ToList()
            .AsReadOnly();
        var patchedGraph  = graph with { Blocks = patchedBlocks };
        var patchedIr     = ir with { Graphs = new[] { patchedGraph } };

        Stage6_Lower.Run(patchedIr, CompilerMode.Debug, sink);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP9001);
    }

    [Fact]
    public void Library_WithFunctionGraph_NoErrors()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("Compute", g => g.Entry().Return())
            .Build();

        var sink   = new DiagnosticSink();
        var ir     = BuildAndScheduleLibrary(asset, sink);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);

        Assert.DoesNotContain(sink.All, d => d.IsError);
        Assert.NotEmpty(lowered.Graphs);
    }
}

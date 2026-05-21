using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Golden IR snapshots: run Stages 1-5 on sample assets and compare IrPrinter output
/// against stored snapshots. Use BLUEPRINT_REGENERATE_SNAPSHOTS=1 to (re)generate.
/// </summary>
public sealed class GoldenIrTests
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

    private static (IrAsset ir, DiagnosticSink sink) ScheduleAsset(string assetName)
    {
        var asset   = TestData.LoadAsset(assetName);
        var opts    = DefaultOptions();
        var sink    = new DiagnosticSink();
        var ctx     = new ValidationContext(sink, opts);

        Stage2_Validate.Run(asset, ctx);
        var normalized = Stage3_Normalize.Run(asset, ctx);
        var typed      = Stage4_TypeResolve.Run(normalized, ctx);
        var ir         = Stage5_Schedule.Run(typed, ctx);

        return (ir, sink);
    }

    [Theory]
    [InlineData(TestData.SampleAssets.LibraryMath)]
    [InlineData(TestData.SampleAssets.InstanceCounter)]
    [InlineData(TestData.SampleAssets.MoveToAndFire)]
    public void Schedule_ProducesExpectedIr(string assetName)
    {
        var (ir, _) = ScheduleAsset(assetName);

        var actual = IrPrinter.PrettyPrint(ir);
        TestData.ReadOrRegenerateSnapshot($"Schedule/{assetName}.ir.txt", actual);
    }

    [Theory]
    [InlineData(TestData.SampleAssets.LibraryMath)]
    [InlineData(TestData.SampleAssets.InstanceCounter)]
    [InlineData(TestData.SampleAssets.MoveToAndFire)]
    public void Schedule_IsDeterministic(string assetName)
    {
        // Run the full pipeline twice; both IR pretty-prints must be identical.
        var (ir1, _) = ScheduleAsset(assetName);
        var (ir2, _) = ScheduleAsset(assetName);

        Assert.Equal(IrPrinter.PrettyPrint(ir1), IrPrinter.PrettyPrint(ir2));
    }

    // ---- BP4001: Unconnected data pin ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP4001")]
    public void Schedule_UnconnectedDataPin_EmitsBP4001()
    {
        // Build a Library asset with a FunctionCallNode that has an input data pin
        // with no incoming link (unconnected).
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var callId  = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var execE   = Guid.NewGuid();
        var execC1  = Guid.NewGuid();
        var execC2  = Guid.NewGuid();
        var execR   = Guid.NewGuid();
        var unconnPin = Guid.NewGuid();

        using var asset = new Assets_PinHelper();
        var graph = new Hrot.Blueprints.Core.Assets.Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = Hrot.Blueprints.Core.Assets.GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Hrot.Blueprints.Core.Assets.Node>
            {
                new Hrot.Blueprints.Core.Assets.EventEntryNode { Id = entryId,
                    Pins = new() { new Hrot.Blueprints.Core.Assets.Pin
                        { Id = execE, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new Hrot.Blueprints.Core.Assets.FunctionCallNode
                {
                    Id = callId,
                    TargetTypeId = "System.Math",
                    MethodName = "Abs",
                    IsPure = false,
                    Pins = new()
                    {
                        new Hrot.Blueprints.Core.Assets.Pin { Id = execC1, Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Hrot.Blueprints.Core.Assets.Pin { Id = execC2, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        // Data input with no incoming link -> BP4001
                        new Hrot.Blueprints.Core.Assets.Pin { Id = unconnPin, Name = "value", Direction = "In", IsExec = false,
                            TypeRef = new Hrot.Blueprints.Core.Assets.BlueprintTypeRef { TypeId = "System.Double" } },
                    },
                },
                new Hrot.Blueprints.Core.Assets.ReturnNode { Id = retId, Status = Hrot.Blueprints.Core.Assets.NodeStatus.Success,
                    Pins = new() { new Hrot.Blueprints.Core.Assets.Pin
                        { Id = execR, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Hrot.Blueprints.Core.Assets.Link>
            {
                new() { FromNodeId = entryId, FromPinId = execE,  ToNodeId = callId, ToPinId = execC1 },
                new() { FromNodeId = callId,  FromPinId = execC2, ToNodeId = retId,  ToPinId = execR  },
                // No link to unconnPin
            },
        };

        var bp = new Hrot.Blueprints.Core.Assets.BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "LibUnconn",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Hrot.Blueprints.Core.Assets.Header { SubsystemType = "Hrot.Blueprints", SchemaVersion = "1.0" },
        };

        var opts   = DefaultOptions();
        var sink   = new DiagnosticSink();
        var ctx    = new ValidationContext(sink, opts);
        var typed  = new TypedAsset(bp,
            new Dictionary<Guid, IrTypeRef>(),
            new Dictionary<Guid, IrTypeRef>());

        Stage5_Schedule.Run(typed, ctx);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP4001);
    }

    // ---- BP4004: Unknown impure node kind ------------------------------

    [Fact]
    [CoversDiagnosticCode("BP4004")]
    public void Schedule_UnknownImpureNode_EmitsBP4004()
    {
        // Build a graph with an UnknownNode (a type not handled by Stage5).
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var unknId  = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var execE   = Guid.NewGuid();
        var execU1  = Guid.NewGuid();
        var execU2  = Guid.NewGuid();
        var execR   = Guid.NewGuid();

        // Inject a node type that Stage5 doesn't recognize as pure or known impure.
        // CastNode is handled as pure (its output is computed via data input), but
        // if we put it in exec chain as an impure node it may trigger BP4004.
        // Use a custom unsupported node type via direct Node subclass.
        var unknownNode = new UnsupportedTestNode
        {
            Id   = unknId,
            Pins = new()
            {
                new Hrot.Blueprints.Core.Assets.Pin { Id = execU1, Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                new Hrot.Blueprints.Core.Assets.Pin { Id = execU2, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
            },
        };

        var graph = new Hrot.Blueprints.Core.Assets.Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = Hrot.Blueprints.Core.Assets.GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Hrot.Blueprints.Core.Assets.Node> { new Hrot.Blueprints.Core.Assets.EventEntryNode
                    { Id = entryId, Pins = new() { new Hrot.Blueprints.Core.Assets.Pin
                        { Id = execE, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                unknownNode,
                new Hrot.Blueprints.Core.Assets.ReturnNode { Id = retId, Status = Hrot.Blueprints.Core.Assets.NodeStatus.Success,
                    Pins = new() { new Hrot.Blueprints.Core.Assets.Pin
                        { Id = execR, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Hrot.Blueprints.Core.Assets.Link>
            {
                new() { FromNodeId = entryId, FromPinId = execE,  ToNodeId = unknId, ToPinId = execU1 },
                new() { FromNodeId = unknId,  FromPinId = execU2, ToNodeId = retId,  ToPinId = execR  },
            },
        };

        var bp = new Hrot.Blueprints.Core.Assets.BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "LibUnk",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Hrot.Blueprints.Core.Assets.Header { SubsystemType = "Hrot.Blueprints", SchemaVersion = "1.0" },
        };

        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);
        var typed = new TypedAsset(bp,
            new Dictionary<Guid, IrTypeRef>(),
            new Dictionary<Guid, IrTypeRef>());

        Stage5_Schedule.Run(typed, ctx);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP4004);
    }

    // Helper: a Node subclass not known to Stage5.
    private sealed class UnsupportedTestNode : Hrot.Blueprints.Core.Assets.Node { }

    // Helper: IDisposable wrapper (unused) to work around `using` keyword scope.
    private sealed class Assets_PinHelper : IDisposable { public void Dispose() { } }
}

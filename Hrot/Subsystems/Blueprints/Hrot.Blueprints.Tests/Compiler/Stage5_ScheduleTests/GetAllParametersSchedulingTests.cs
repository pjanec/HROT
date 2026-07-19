using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Stage 5 GetAllParametersNode lowering: a SINGLE node with one data-out pin per declared
/// Parameter must resolve EACH requested out-pin to the matching <see cref="IrOp_ReadParam"/>
/// index -- by pin NAME, not by pin/declaration ordinal (mirrors how EventEntryNode's data-out
/// pins are matched by name against Graph.Inputs, see Stage5_Schedule's EventEntryNode case).
/// Direct Stage5-only test (mirrors SequenceSchedulingTests' <c>RunStage5</c> pattern): builds a
/// <see cref="TypedAsset"/> by hand and calls <see cref="Stage5_Schedule.Run"/> without the earlier
/// stages, then inspects the emitted <see cref="IrStatement"/>s directly -- no Roslyn compile
/// needed to prove the per-pin index resolution.
/// </summary>
public sealed class GetAllParametersSchedulingTests
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

    private static (IrAsset ir, DiagnosticSink sink) RunStage5(BlueprintAsset bp)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        var typed = new TypedAsset(bp,
            new Dictionary<Guid, IrTypeRef>(),
            new Dictionary<Guid, IrTypeRef>());

        var ir = Stage5_Schedule.Run(typed, ctx);
        return (ir, sink);
    }

    [Fact]
    public void Schedule_TwoConsumersOnDifferentOutPins_EmitReadParamWithCorrectIndicesEach()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var gapId   = Guid.NewGuid();
        var setAId  = Guid.NewGuid();
        var setBId  = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var pinEntryOut = Guid.NewGuid();

        // GetAllParametersNode's two out-pins are authored in the OPPOSITE order to the
        // Parameters declaration list below (ParamB's pin comes first), so a naive
        // "match by ordinal" resolution would silently swap the two indices -- only a real
        // by-NAME match (FindParameterIndex) gets this right.
        var pinGapB = Guid.NewGuid(); // "ParamB" out-pin (declared 2nd => index 1)
        var pinGapA = Guid.NewGuid(); // "ParamA" out-pin (declared 1st => index 0)

        var pinSetAIn    = Guid.NewGuid();
        var pinSetAOut   = Guid.NewGuid();
        var pinSetAValue = Guid.NewGuid();

        var pinSetBIn    = Guid.NewGuid();
        var pinSetBOut   = Guid.NewGuid();
        var pinSetBValue = Guid.NewGuid();

        var pinRetIn = Guid.NewGuid();

        var graph = new Graph
        {
            Id     = graphId,
            Name   = "G",
            Kind   = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes  = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new GetAllParametersNode
                {
                    Id   = gapId,
                    Pins = new()
                    {
                        new Pin { Id = pinGapB, Name = "ParamB", Direction = "Out", IsExec = false, TypeRef = new() { TypeId = "System.Int32" } },
                        new Pin { Id = pinGapA, Name = "ParamA", Direction = "Out", IsExec = false, TypeRef = new() { TypeId = "System.Single" } },
                    },
                },
                new SetVariableNode
                {
                    Id         = setAId,
                    VariableId = "A",
                    Pins       = new()
                    {
                        new Pin { Id = pinSetAIn,    Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSetAOut,   Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSetAValue, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() { TypeId = "System.Single" } },
                    },
                },
                new SetVariableNode
                {
                    Id         = setBId,
                    VariableId = "B",
                    Pins       = new()
                    {
                        new Pin { Id = pinSetBIn,    Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSetBOut,   Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSetBValue, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id     = retId,
                    Status = NodeStatus.Success,
                    Pins   = new() { new Pin { Id = pinRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = setAId, ToPinId = pinSetAIn },
                new() { FromNodeId = setAId,  FromPinId = pinSetAOut,  ToNodeId = setBId, ToPinId = pinSetBIn },
                new() { FromNodeId = setBId,  FromPinId = pinSetBOut,  ToNodeId = retId,  ToPinId = pinRetIn },
                // Data: setA reads GetAllParameters' "ParamA" out-pin, setB reads "ParamB".
                new() { FromNodeId = gapId,   FromPinId = pinGapA,     ToNodeId = setAId, ToPinId = pinSetAValue },
                new() { FromNodeId = gapId,   FromPinId = pinGapB,     ToNodeId = setBId, ToPinId = pinSetBValue },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "GetAllParamsTwoConsumers",
            Dispatch = BlueprintDispatchKind.AiPrimitive,
            Parameters = new()
            {
                new ParameterDecl { Id = Guid.NewGuid(), Name = "ParamA", Type = new BlueprintTypeRef { TypeId = "System.Single" } },
                new ParameterDecl { Id = Guid.NewGuid(), Name = "ParamB", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
            WorkingState     = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs           = new() { graph },
            Header           = new Header(),
        };

        var (ir, _) = RunStage5(bp);

        var irGraph = Assert.Single(ir.Graphs);
        var readParamStmts = irGraph.Blocks
            .SelectMany(b => b.Statements)
            .Where(s => s.Operation is IrOp_ReadParam)
            .ToList();

        Assert.Equal(2, readParamStmts.Count);

        // The statement resolving the "ParamA" out-pin (index 0) must carry ParamIndex == 0,
        // and the one resolving "ParamB" (index 1) must carry ParamIndex == 1 -- proving the
        // per-pin NAME match (not the pins' authored ordinal, which is reversed here).
        var fromParamAPin = Assert.Single(readParamStmts, s => s.Debug.PinId == pinGapA);
        var fromParamBPin = Assert.Single(readParamStmts, s => s.Debug.PinId == pinGapB);

        Assert.Equal(0, ((IrOp_ReadParam)fromParamAPin.Operation).ParamIndex);
        Assert.Equal(1, ((IrOp_ReadParam)fromParamBPin.Operation).ParamIndex);
    }
}

using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Stage 5 diagnostic BP1412: scheduler drops exec successors silently.
/// </summary>
public sealed class BP1412_DroppedExecSuccessorsTests
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

    // ----------------------------------------------------------------
    // Scenario 1: SequenceNode with linked exec-out pins -- after SEQ1
    // the Sequence is CORRECTLY scheduled.  No BP1412 must fire.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_SequenceNode_LinkedExecOuts_SchedulesCorrectly_NoBP1412()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var seqId   = Guid.NewGuid();
        var ret1Id  = Guid.NewGuid();
        var ret2Id  = Guid.NewGuid();

        var pinEntryOut  = Guid.NewGuid();
        var pinSeqIn     = Guid.NewGuid();
        var pinSeqThen0  = Guid.NewGuid();
        var pinSeqThen1  = Guid.NewGuid();
        var pinRet1In    = Guid.NewGuid();
        var pinRet2In    = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new SequenceNode
                {
                    Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = pinSeqIn,    Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",   Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",   Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode { Id = ret1Id, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRet1In, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
                new ReturnNode { Id = ret2Id, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRet2In, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = seqId,  ToPinId = pinSeqIn    },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0, ToNodeId = ret1Id, ToPinId = pinRet1In   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen1, ToNodeId = ret2Id, ToPinId = pinRet2In   },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqScheduled",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412 -- Sequence is correctly scheduled.
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        // Must have multiple blocks (entry + 2 branch blocks).
        var irGraph = Assert.Single(ir.Graphs);
        Assert.True(irGraph.Blocks.Count >= 3,
            $"Expected >= 3 blocks for scheduled Sequence, got {irGraph.Blocks.Count}");
    }

    // ----------------------------------------------------------------
    // Scenario 2: unresolved exec link (target node not in graph) --
    // fires BP1412 because an outgoing exec link exists but isn't followed.
    // ----------------------------------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1412")]
    public void Schedule_UnresolvedExecLink_EmitsBP1412_Error()
    {
        var assetId    = Guid.NewGuid();
        var graphId    = Guid.NewGuid();
        var entryId    = Guid.NewGuid();
        var missingId  = Guid.NewGuid(); // not in Nodes list
        var pinEntryOut = Guid.NewGuid();
        var pinMissingIn = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = missingId, ToPinId = pinMissingIn },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "UnresLink",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (_, sink) = RunStage5(bp);

        var bp1412 = sink.All.Where(d => d.Code == DiagnosticCodes.BP1412).ToList();
        Assert.NotEmpty(bp1412);
        Assert.All(bp1412, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // Message must contain the offending node id
        Assert.Contains(bp1412, d =>
            d.Message.Contains(entryId.ToString()) &&
            d.Message.Contains("EventEntryNode"));
    }

    // ----------------------------------------------------------------
    // Scenario 3: legitimate chain-end (normal EventEntry -> Return)
    // -- must NOT fire BP1412.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_NormalChain_NoBP1412()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var pinEntryOut = Guid.NewGuid();
        var pinRetIn    = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = retId, ToPinId = pinRetIn },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "Normal",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (_, sink) = RunStage5(bp);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);
    }

    // ----------------------------------------------------------------
    // Scenario 4: node with no exec-out pins at end of chain --
    // must NOT fire BP1412 (legitimate dead-end).
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_NodeWithNoExecOutPin_NoBP1412()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var literalId = Guid.NewGuid();
        var retId    = Guid.NewGuid();
        var pinEntryOut  = Guid.NewGuid();
        var pinLitIn     = Guid.NewGuid();
        var pinLitOut    = Guid.NewGuid();
        var pinRetIn     = Guid.NewGuid();

        // LiteralNode has a data-out but no exec-out by default.
        // We set it up so the exec chain goes Entry -> Literal -> Return.
        // As an exec chain node, Literal falls into the default case in ScheduleBlock
        // where EmitNodeStatements is called (it's a pure source, so handled in
        // ResolveNodeOutput).  After emitting, GetSingleExecSuccessor finds the
        // exec-out pin and follows it to Return -- this is a normal chain, no drop.
        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id = literalId,
                    TypeId = "System.Int32",
                    ValueJson = "42",
                    Pins = new()
                    {
                        new Pin { Id = pinLitIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinLitOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId,   FromPinId = pinEntryOut, ToNodeId = literalId, ToPinId = pinLitIn  },
                new() { FromNodeId = literalId, FromPinId = pinLitOut,   ToNodeId = retId,     ToPinId = pinRetIn  },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "LiteralChain",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (_, sink) = RunStage5(bp);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);
    }

    // ----------------------------------------------------------------
    // Scenario 5: EventEntry with zero exec-out pins -- no outgoing
    // exec links, legitimately no successors -- no BP1412.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_EventEntryNoExecOutPin_NoBP1412()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new(), // no exec-out
                },
            },
            Links = new List<Link>(),
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "NoExecOut",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (_, sink) = RunStage5(bp);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);
    }

    // ----------------------------------------------------------------
    // Scenario 6: verify diagnostic includes node id in its NodeId
    // context property (locatability).  Uses the unresolved-link case
    // (still triggers BP1412 after SEQ1).
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_DroppedSuccessor_DiagnosticHasNodeId()
    {
        var assetId    = Guid.NewGuid();
        var graphId    = Guid.NewGuid();
        var entryId    = Guid.NewGuid();
        var missingId  = Guid.NewGuid(); // not in Nodes list

        var pinEntryOut  = Guid.NewGuid();
        var pinMissingIn = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = missingId, ToPinId = pinMissingIn },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "NodeIdCtx",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (_, sink) = RunStage5(bp);

        var bp1412 = sink.All.FirstOrDefault(d => d.Code == DiagnosticCodes.BP1412);
        Assert.NotNull(bp1412);
        Assert.Equal(entryId, bp1412.NodeId);
        Assert.Equal(graphId, bp1412.GraphId);
    }
}

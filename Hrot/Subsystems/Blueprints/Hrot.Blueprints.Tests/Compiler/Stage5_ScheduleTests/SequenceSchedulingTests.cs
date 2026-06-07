using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Stage 5 Sequence scheduling: SequenceNode runs its connected Then outputs
/// in order (Then0, Then1, ...).  Tests IR structure, diagnostic behavior,
/// and nested/latent cases.
/// </summary>
public sealed class SequenceSchedulingTests
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
    // Scenario 1: Two synchronous branches run in order.
    //   EventEntry -> Sequence(Then0->SetVarA, Then1->SetVarB)
    //   Assert IR chains correctly and NO BP1412.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_TwoSequenceBranches_ChainsInOrder_NoBP1412()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();
        var svAId    = Guid.NewGuid();
        var svBId    = Guid.NewGuid();
        var retId    = Guid.NewGuid();

        var pinEntryOut = Guid.NewGuid();
        var pinSeqIn    = Guid.NewGuid();
        var pinSeqThen0 = Guid.NewGuid();
        var pinSeqThen1 = Guid.NewGuid();
        var pinSvAIn    = Guid.NewGuid();
        var pinSvAOut   = Guid.NewGuid();
        var pinSvBIn    = Guid.NewGuid();
        var pinSvBOut   = Guid.NewGuid();
        var pinRetIn    = Guid.NewGuid();

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
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = svAId, VariableId = "A",
                    Pins = new()
                    {
                        new Pin { Id = pinSvAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSvAOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = svBId, VariableId = "B",
                    Pins = new()
                    {
                        new Pin { Id = pinSvBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSvBOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = seqId,   ToPinId = pinSeqIn   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0, ToNodeId = svAId,   ToPinId = pinSvAIn   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen1, ToNodeId = svBId,   ToPinId = pinSvBIn   },
                // svA and svB exec-out pins have no outgoing link -- legitimate chain end.
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqTwoSync",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412 (Sequence is correctly scheduled).
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        // Verify IR structure: entry block Gotos seq_then0,
        // seq_then0 block contains SetVariable A and Gotos seq_then1,
        // seq_then1 block contains SetVariable B and ends fall-through.
        var irGraph = Assert.Single(ir.Graphs);
        Assert.True(irGraph.Blocks.Count >= 3,
            $"Expected >= 3 blocks (entry + 2 branches), got {irGraph.Blocks.Count}");

        // Entry block should terminate with Goto (to first branch).
        var entryBlock = irGraph.Blocks[0];
        var entryGoto = Assert.IsType<IrTerm_Goto>(entryBlock.Terminator);

        // Find the seq_then0 block (target of entry's Goto).
        var then0Block = irGraph.Blocks.FirstOrDefault(b => b.Id.Value == entryGoto.Target.Value);
        Assert.NotNull(then0Block);

        // seq_then0 should end with Goto to seq_then1 (chained).
        var then0Goto = Assert.IsType<IrTerm_Goto>(then0Block.Terminator);

        // Find the seq_then1 block.
        var then1Block = irGraph.Blocks.FirstOrDefault(b => b.Id.Value == then0Goto.Target.Value);
        Assert.NotNull(then1Block);

        // seq_then1 should end with fall-through (last branch, no further chain).
        Assert.IsType<IrTerm_FallThrough>(then1Block.Terminator);
    }

    // ----------------------------------------------------------------
    // Scenario 2: Unconnected Then1 pin -- only Then0 runs.
    //   No BP1412, no crash, blocks allocated only for connected branch.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_UnconnectedThenPin_OnlyConnectedBranchesScheduled()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();
        var retId    = Guid.NewGuid();

        var pinEntryOut = Guid.NewGuid();
        var pinSeqIn    = Guid.NewGuid();
        var pinSeqThen0 = Guid.NewGuid();
        var pinSeqThen1 = Guid.NewGuid(); // Then1 pin exists but no link
        var pinRetIn    = Guid.NewGuid();

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
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = seqId,   ToPinId = pinSeqIn   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0, ToNodeId = retId,   ToPinId = pinRetIn   },
                // Then1 is NOT linked.
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqUnconnPin",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412.
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        // Only one branch block allocated.
        var irGraph = Assert.Single(ir.Graphs);
        // entry + 1 branch block = 2 blocks.
        Assert.Equal(2, irGraph.Blocks.Count);
    }

    // ----------------------------------------------------------------
    // Scenario 3: Branch ends in Return short-circuits.
    //   Then0 -> Return (Success), Then1 -> Return (Success).
    //   Then0's Return terminates; Then1's code NOT reachable
    //   (Then0 block's terminator is Return, not Goto to Then1).
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_Then0Returns_ShortCircuits_Then1NotReachable()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();
        var ret0Id   = Guid.NewGuid();
        var ret1Id   = Guid.NewGuid();

        var pinEntryOut = Guid.NewGuid();
        var pinSeqIn    = Guid.NewGuid();
        var pinSeqThen0 = Guid.NewGuid();
        var pinSeqThen1 = Guid.NewGuid();
        var pinRet0In   = Guid.NewGuid();
        var pinRet1In   = Guid.NewGuid();

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
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode { Id = ret0Id, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRet0In, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
                new ReturnNode { Id = ret1Id, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRet1In, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = seqId,   ToPinId = pinSeqIn    },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0, ToNodeId = ret0Id,  ToPinId = pinRet0In   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen1, ToNodeId = ret1Id,  ToPinId = pinRet1In   },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqRetShort",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412.
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        var irGraph = Assert.Single(ir.Graphs);

        // seq_then0 block should end with IrTerm_ReturnStatus (not Goto).
        // Return short-circuits: Then0's block terminates with Return,
        // and Then1's block is never enqueued after Then0's Return (but
        // it IS enqueued directly by ScheduleSequenceNode as a successor).
        // Both branch blocks get enqueued; Then0's block gets Return,
        // Then1's block gets its own Return.  The key invariant: Then0
        // has a ReturnStatus terminator (not a Goto to Then1).
        foreach (var block in irGraph.Blocks)
        {
            if (block.Label.Contains("seq_") && block.Label.Contains("_then0"))
            {
                // Then0 contains a ReturnNode -> terminator should be ReturnStatus.
                var rt = Assert.IsType<IrTerm_ReturnStatus>(block.Terminator);
                Assert.Equal(NodeStatus.Success, rt.Status);
            }
        }

        // Then1 block is still present (scheduled independently) but Then0
        // does NOT chain to it (Then0 has ReturnStatus, not Goto to Then1).
    }

    // ----------------------------------------------------------------
    // Scenario 4: Nested Sequence.
    //   Then1's successor is itself a Sequence with Then0->A, Then1->B.
    //   Both inner branches should run after Then0 of outer Sequence.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_NestedSequence_ChainsInnerBranchesAfterOuterThen0()
    {
        var assetId    = Guid.NewGuid();
        var graphId    = Guid.NewGuid();
        var entryId    = Guid.NewGuid();
        var outerSeqId = Guid.NewGuid();
        var innerSeqId = Guid.NewGuid();
        var aId        = Guid.NewGuid();
        var bId        = Guid.NewGuid();
        var cId        = Guid.NewGuid();

        var pinEntryOut      = Guid.NewGuid();
        var pinOuterIn       = Guid.NewGuid();
        var pinOuterThen0    = Guid.NewGuid();
        var pinOuterThen1    = Guid.NewGuid();
        var pinAIn           = Guid.NewGuid();
        var pinAOut          = Guid.NewGuid();
        var pinInnerIn       = Guid.NewGuid();
        var pinInnerThen0    = Guid.NewGuid();
        var pinInnerThen1    = Guid.NewGuid();
        var pinBIn           = Guid.NewGuid();
        var pinBOut          = Guid.NewGuid();
        var pinCIn           = Guid.NewGuid();
        var pinCOut          = Guid.NewGuid();

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
                    Id = outerSeqId,
                    Pins = new()
                    {
                        new Pin { Id = pinOuterIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinOuterThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinOuterThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = aId, VariableId = "A",
                    Pins = new()
                    {
                        new Pin { Id = pinAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinAOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
                new SequenceNode
                {
                    Id = innerSeqId,
                    Pins = new()
                    {
                        new Pin { Id = pinInnerIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinInnerThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinInnerThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = bId, VariableId = "B",
                    Pins = new()
                    {
                        new Pin { Id = pinBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinBOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = cId, VariableId = "C",
                    Pins = new()
                    {
                        new Pin { Id = pinCIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinCOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                // Entry -> outer Seq
                new() { FromNodeId = entryId,    FromPinId = pinEntryOut,   ToNodeId = outerSeqId, ToPinId = pinOuterIn    },
                // outer Then0 -> SetVariable A
                new() { FromNodeId = outerSeqId, FromPinId = pinOuterThen0, ToNodeId = aId,        ToPinId = pinAIn        },
                // outer Then1 -> inner Seq
                new() { FromNodeId = outerSeqId, FromPinId = pinOuterThen1, ToNodeId = innerSeqId, ToPinId = pinInnerIn    },
                // inner Then0 -> SetVariable B
                new() { FromNodeId = innerSeqId, FromPinId = pinInnerThen0, ToNodeId = bId,        ToPinId = pinBIn        },
                // inner Then1 -> SetVariable C
                new() { FromNodeId = innerSeqId, FromPinId = pinInnerThen1, ToNodeId = cId,        ToPinId = pinCIn        },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqNested",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412.
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        var irGraph = Assert.Single(ir.Graphs);
        // entry + outer_then0 + outer_then1 + inner_then0 + inner_then1 = 5 blocks
        Assert.True(irGraph.Blocks.Count >= 5,
            $"Expected >= 5 blocks, got {irGraph.Blocks.Count}");
    }

    // ----------------------------------------------------------------
    // Scenario 5: Latent node inside a Sequence branch.
    //   Sequence Then0 is a latent node (WaitForChannel).
    //   Verify either correct propagation OR BP1413 deferral.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_LatentInSequenceBranch_PropagatesOrEmitsBP1413()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();
        var wfcId    = Guid.NewGuid();
        var svId     = Guid.NewGuid();
        var retId    = Guid.NewGuid();

        var pinEntryOut  = Guid.NewGuid();
        var pinSeqIn     = Guid.NewGuid();
        var pinSeqThen0  = Guid.NewGuid();
        var pinSeqThen1  = Guid.NewGuid();
        var pinWfcIn     = Guid.NewGuid();
        var pinWfcOut    = Guid.NewGuid();
        var pinSvIn      = Guid.NewGuid();
        var pinSvOut     = Guid.NewGuid();
        var pinRetIn     = Guid.NewGuid();

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
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new WaitForChannelNode
                {
                    Id = wfcId,
                    ChannelType = "LocomotionChannel",
                    Pins = new()
                    {
                        new Pin { Id = pinWfcIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinWfcOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = svId, VariableId = "A",
                    Pins = new()
                    {
                        new Pin { Id = pinSvIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSvOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut,  ToNodeId = seqId,  ToPinId = pinSeqIn    },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0,  ToNodeId = wfcId,  ToPinId = pinWfcIn    },
                new() { FromNodeId = wfcId,   FromPinId = pinWfcOut,    ToNodeId = retId,  ToPinId = pinRetIn    },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen1,  ToNodeId = svId,   ToPinId = pinSvIn     },
                new() { FromNodeId = svId,    FromPinId = pinSvOut,     ToNodeId = retId,  ToPinId = pinRetIn    },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqLatent",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412 (latent propagation should work via _fallThroughTarget).
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        // If the latent case is deferred, BP1413 should be present.
        // Otherwise (implemented correctly), no BP1413 and the
        // resume block should chain to Then1.
        var bp1413 = sink.All.Where(d => d.Code == DiagnosticCodes.BP1413).ToList();
        if (bp1413.Count > 0)
        {
            // Deferred case: BP1413 is emitted.
            Assert.All(bp1413, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        }
        else
        {
            // Implemented case: verify resume block chains to Then1.
            var irGraph = Assert.Single(ir.Graphs);
            // Should have: entry, seq_then0 (pre-suspend), resume, seq_then1.
            Assert.True(irGraph.Blocks.Count >= 4,
                $"Expected >= 4 blocks (entry + pre-suspend + resume + then1), got {irGraph.Blocks.Count}");
        }
    }

    // ----------------------------------------------------------------
    // Scenario 6: Zero connected branches -- Sequence with no exec
    //   links seals as fall-through, no BP1412.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_ZeroConnectedBranches_SealsFallThrough_NoBP1412()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();

        var pinEntryOut = Guid.NewGuid();
        var pinSeqIn    = Guid.NewGuid();
        var pinSeqThen0 = Guid.NewGuid();
        var pinSeqThen1 = Guid.NewGuid();

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
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = seqId, ToPinId = pinSeqIn },
                // Neither Then0 nor Then1 are linked.
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqZeroBranch",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412 — zero connected branches is legitimate.
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        // Single block with fall-through terminator.
        var irGraph = Assert.Single(ir.Graphs);
        Assert.Single(irGraph.Blocks);
        Assert.IsType<IrTerm_FallThrough>(irGraph.Blocks[0].Terminator);
    }

    // ----------------------------------------------------------------
    // Scenario 7: Branch node inside Sequence branch — propagation.
    //   Sequence Then0 is a Branch; both true/false branches should
    //   continue to Then1 after the branch completes.
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_BranchInsideSequence_PropagatesFallThrough()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();
        var bnId     = Guid.NewGuid();
        var svTId    = Guid.NewGuid();
        var svFId    = Guid.NewGuid();
        var sv1Id    = Guid.NewGuid();
        var retId    = Guid.NewGuid();

        var pinEntryOut  = Guid.NewGuid();
        var pinSeqIn     = Guid.NewGuid();
        var pinSeqThen0  = Guid.NewGuid();
        var pinSeqThen1  = Guid.NewGuid();
        var pinBnIn      = Guid.NewGuid();
        var pinBnTrue    = Guid.NewGuid();
        var pinBnFalse   = Guid.NewGuid();
        var pinBnCond    = Guid.NewGuid();
        var pinSvTIn     = Guid.NewGuid();
        var pinSvTOut    = Guid.NewGuid();
        var pinSvFIn     = Guid.NewGuid();
        var pinSvFOut    = Guid.NewGuid();
        var pinSv1In     = Guid.NewGuid();
        var pinSv1Out    = Guid.NewGuid();
        var pinRetIn     = Guid.NewGuid();

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
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new BranchNode
                {
                    Id = bnId,
                    Pins = new()
                    {
                        new Pin { Id = pinBnIn,    Name = "ExecIn",      Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinBnTrue,  Name = "ExecOutTrue",  Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinBnFalse, Name = "ExecOutFalse", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinBnCond,  Name = "Condition",   Direction = "In",  IsExec = false, TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = svTId, VariableId = "T",
                    Pins = new()
                    {
                        new Pin { Id = pinSvTIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSvTOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = svFId, VariableId = "F",
                    Pins = new()
                    {
                        new Pin { Id = pinSvFIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSvFOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
                new SetVariableNode { Id = sv1Id, VariableId = "Last",
                    Pins = new()
                    {
                        new Pin { Id = pinSv1In,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSv1Out, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    },
                },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pinRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut,  ToNodeId = seqId,  ToPinId = pinSeqIn    },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0,  ToNodeId = bnId,   ToPinId = pinBnIn     },
                new() { FromNodeId = bnId,    FromPinId = pinBnTrue,    ToNodeId = svTId,  ToPinId = pinSvTIn    },
                new() { FromNodeId = bnId,    FromPinId = pinBnFalse,   ToNodeId = svFId,  ToPinId = pinSvFIn    },
                new() { FromNodeId = svTId,   FromPinId = pinSvTOut,    ToNodeId = retId,  ToPinId = pinRetIn    },
                new() { FromNodeId = svFId,   FromPinId = pinSvFOut,    ToNodeId = retId,  ToPinId = pinRetIn    },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen1,  ToNodeId = sv1Id,  ToPinId = pinSv1In    },
                new() { FromNodeId = sv1Id,   FromPinId = pinSv1Out,    ToNodeId = retId,  ToPinId = pinRetIn    },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SeqBranchInside",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        // No BP1412.
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        var irGraph = Assert.Single(ir.Graphs);
        // entry + seq_then0 (branch) + branch_true + branch_false + seq_then1 = at least 5
        Assert.True(irGraph.Blocks.Count >= 5,
            $"Expected >= 5 blocks, got {irGraph.Blocks.Count}");
    }

    // ----------------------------------------------------------------
    // Scenario 8 (lead-added, strong propagation check): latent in Then0
    // that FALLS THROUGH (no Return) — the resume block must Goto the
    // Then1 block, making Then1 REACHABLE.  ScheduleSequenceNode always
    // *schedules* the Then1 block; propagation is what makes something
    // *jump* to it.  If propagation were broken, Then1 would be an
    // unreachable orphan (no incoming Goto).
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_LatentInSequence_FallThrough_ResumeReachesThen1()
    {
        var entryId = Guid.NewGuid();
        var seqId   = Guid.NewGuid();
        var wfcId   = Guid.NewGuid();
        var svId    = Guid.NewGuid();

        var pinEntryOut = Guid.NewGuid();
        var pinSeqIn    = Guid.NewGuid();
        var pinSeqThen0 = Guid.NewGuid();
        var pinSeqThen1 = Guid.NewGuid();
        var pinWfcIn    = Guid.NewGuid();
        var pinWfcOut   = Guid.NewGuid();   // intentionally UNCONNECTED -> falls through
        var pinSvIn     = Guid.NewGuid();

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "G", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
                new WaitForChannelNode { Id = wfcId, ChannelType = "LocomotionChannel",
                    Pins = new()
                    {
                        new Pin { Id = pinWfcIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinWfcOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
                new SetVariableNode { Id = svId, VariableId = "A",
                    Pins = new()
                    {
                        new Pin { Id = pinSvIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                        new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = seqId, ToPinId = pinSeqIn   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0, ToNodeId = wfcId, ToPinId = pinWfcIn  },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen1, ToNodeId = svId,  ToPinId = pinSvIn   },
                // wfc ExecOut intentionally unconnected -> branch 0 falls through after the latent resumes.
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "SeqLatentReach", Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph }, Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1413);

        var irGraph = Assert.Single(ir.Graphs);
        var then1 = Assert.Single(irGraph.Blocks, b => b.Label.Contains("_then1"));
        var gotoTargets = irGraph.Blocks
            .Select(b => b.Terminator).OfType<IrTerm_Goto>()
            .Select(t => t.Target.Value).ToList();
        // Propagation correctness: the latent resume must Goto Then1 (Then1 reachable),
        // NOT leave it orphaned. Entry Gotos Then0, so a SECOND Goto must target Then1.
        Assert.Contains(then1.Id.Value, gotoTargets);
    }

    // ----------------------------------------------------------------
    // Scenario 9 (lead-added): Branch in Then0 whose arms FALL THROUGH
    // (no Return) — both arms must Goto the Then1 block (reachable).
    // ----------------------------------------------------------------

    [Fact]
    public void Schedule_BranchInSequence_FallThrough_BothArmsReachThen1()
    {
        var entryId = Guid.NewGuid();
        var seqId   = Guid.NewGuid();
        var bnId    = Guid.NewGuid();
        var svTId   = Guid.NewGuid();
        var svFId   = Guid.NewGuid();
        var sv1Id   = Guid.NewGuid();

        var pinEntryOut = Guid.NewGuid();
        var pinSeqIn    = Guid.NewGuid();
        var pinSeqThen0 = Guid.NewGuid();
        var pinSeqThen1 = Guid.NewGuid();
        var pinBnIn     = Guid.NewGuid();
        var pinBnTrue   = Guid.NewGuid();
        var pinBnFalse  = Guid.NewGuid();
        var pinSvTIn    = Guid.NewGuid();
        var pinSvFIn    = Guid.NewGuid();
        var pinSv1In    = Guid.NewGuid();

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "G", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = pinSeqIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = pinSeqThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
                new BranchNode { Id = bnId,
                    Pins = new()
                    {
                        new Pin { Id = pinBnIn,    Name = "ExecIn",       Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinBnTrue,  Name = "ExecOutTrue",  Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinBnFalse, Name = "ExecOutFalse", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = Guid.NewGuid(), Name = "Condition", Direction = "In", IsExec = false, TypeRef = new() },
                    } },
                new SetVariableNode { Id = svTId, VariableId = "T",
                    Pins = new() { new Pin { Id = pinSvTIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                                   new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SetVariableNode { Id = svFId, VariableId = "F",
                    Pins = new() { new Pin { Id = pinSvFIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                                   new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SetVariableNode { Id = sv1Id, VariableId = "Last",
                    Pins = new() { new Pin { Id = pinSv1In, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                                   new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = seqId,  ToPinId = pinSeqIn   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen0, ToNodeId = bnId,   ToPinId = pinBnIn    },
                new() { FromNodeId = bnId,    FromPinId = pinBnTrue,   ToNodeId = svTId,  ToPinId = pinSvTIn   },
                new() { FromNodeId = bnId,    FromPinId = pinBnFalse,  ToNodeId = svFId,  ToPinId = pinSvFIn   },
                new() { FromNodeId = seqId,   FromPinId = pinSeqThen1, ToNodeId = sv1Id,  ToPinId = pinSv1In   },
                // svT / svF / sv1 ExecOut all unconnected -> fall through.
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "SeqBranchReach", Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph }, Header = new Header(),
        };

        var (ir, sink) = RunStage5(bp);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1412);

        var irGraph = Assert.Single(ir.Graphs);
        var then1 = Assert.Single(irGraph.Blocks, b => b.Label.Contains("_then1"));
        // Both branch arms must fall through to Then1 -> Then1 is the target of TWO Gotos.
        var gotoToThen1 = irGraph.Blocks
            .Select(b => b.Terminator).OfType<IrTerm_Goto>()
            .Count(t => t.Target.Value == then1.Id.Value);
        Assert.True(gotoToThen1 >= 2,
            $"Expected both Branch arms to Goto Then1 (>=2 incoming Gotos), got {gotoToThen1}");
    }
}

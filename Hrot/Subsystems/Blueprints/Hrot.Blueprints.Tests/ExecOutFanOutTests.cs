using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Compiler;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Tests for the Stage 2 V_ExecOutFanOut validator (EXEC1 -- BF-BATCH-EXECFANOUT).
/// Verifies that a single exec-out pin linked to more than one target emits BP1411,
/// that a single link is accepted, and that multi-pin nodes (SequenceNode, BranchNode)
/// whose individual pins each drive exactly one target do NOT produce false positives.
/// </summary>
public sealed class ExecOutFanOutTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

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
    /// Builds a minimal Instance asset with an Event graph that contains:
    ///   EventEntryNode  (exec-out)
    ///   ReturnNodeA     (exec-in)
    ///   ReturnNodeB     (exec-in)
    /// and wires the entry's exec-out to BOTH return nodes (illegal fan-out).
    /// All node Pins are pre-populated so Stage2 structural validators run.
    /// </summary>
    private static (BlueprintAsset asset, Guid entryExecOutPinId)
        BuildFanOutGraph()
    {
        var entryExecOutPinId = Guid.NewGuid();
        var retAExecInPinId   = Guid.NewGuid();
        var retBExecInPinId   = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = entryExecOutPinId, Name = "Out", Direction = "Out", IsExec = true });

        var retA = new ReturnNode { Id = Guid.NewGuid() };
        retA.Pins.Add(new Pin { Id = retAExecInPinId, Name = "In", Direction = "In", IsExec = true });

        var retB = new ReturnNode { Id = Guid.NewGuid() };
        retB.Pins.Add(new Pin { Id = retBExecInPinId, Name = "In", Direction = "In", IsExec = true });

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Event,
            Nodes = { entry, retA, retB },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryExecOutPinId,
                           ToNodeId   = retA.Id,  ToPinId   = retAExecInPinId },
                new Link { FromNodeId = entry.Id, FromPinId = entryExecOutPinId,
                           ToNodeId   = retB.Id,  ToPinId   = retBExecInPinId },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "FanOutTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        return (asset, entryExecOutPinId);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// EXEC1-T1: A single exec-out pin linked to two successors emits BP1411 as an Error.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1411")]
    public void Stage2_ExecOutFanOut_TwoSuccessors_EmitsBP1411_AsError()
    {
        var (asset, _) = BuildFanOutGraph();

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        var bp1411 = sink.All.Where(d => d.Code == DiagnosticCodes.BP1411).ToList();
        Assert.NotEmpty(bp1411);
        Assert.All(bp1411, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    /// <summary>
    /// EXEC1-T2: The same graph with only one link from the exec-out pin produces no BP1411.
    /// </summary>
    [Fact]
    public void Stage2_ExecOutFanOut_SingleSuccessor_NoBP1411()
    {
        var entryExecOutPinId = Guid.NewGuid();
        var retExecInPinId    = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = entryExecOutPinId, Name = "Out", Direction = "Out", IsExec = true });

        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(new Pin { Id = retExecInPinId, Name = "In", Direction = "In", IsExec = true });

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Event,
            Nodes = { entry, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryExecOutPinId,
                           ToNodeId   = ret.Id,   ToPinId   = retExecInPinId },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "SingleSuccessorTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1411);
    }

    /// <summary>
    /// EXEC1-T3: A SequenceNode with two exec-out pins (Then0 and Then1) each linked to
    /// exactly one successor produces no BP1411 -- proving the rule is per-pin, not per-node.
    /// </summary>
    [Fact]
    public void Stage2_ExecOutFanOut_SequenceNode_PerPinOneSuccessor_NoBP1411()
    {
        var entryExecOutPinId = Guid.NewGuid();
        var seqExecInPinId    = Guid.NewGuid();
        var seqThen0PinId     = Guid.NewGuid();
        var seqThen1PinId     = Guid.NewGuid();
        var ret1ExecInPinId   = Guid.NewGuid();
        var ret2ExecInPinId   = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = entryExecOutPinId, Name = "Out",   Direction = "Out", IsExec = true });

        var seq = new SequenceNode { Id = Guid.NewGuid() };
        seq.Pins.Add(new Pin { Id = seqExecInPinId, Name = "In",    Direction = "In",  IsExec = true });
        seq.Pins.Add(new Pin { Id = seqThen0PinId,  Name = "Then0", Direction = "Out", IsExec = true });
        seq.Pins.Add(new Pin { Id = seqThen1PinId,  Name = "Then1", Direction = "Out", IsExec = true });

        var ret1 = new ReturnNode { Id = Guid.NewGuid() };
        ret1.Pins.Add(new Pin { Id = ret1ExecInPinId, Name = "In", Direction = "In", IsExec = true });

        var ret2 = new ReturnNode { Id = Guid.NewGuid() };
        ret2.Pins.Add(new Pin { Id = ret2ExecInPinId, Name = "In", Direction = "In", IsExec = true });

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Event,
            Nodes = { entry, seq, ret1, ret2 },
            Links =
            {
                // entry → seq (one link from entryExecOut)
                new Link { FromNodeId = entry.Id, FromPinId = entryExecOutPinId,
                           ToNodeId   = seq.Id,   ToPinId   = seqExecInPinId },
                // seq.Then0 → ret1 (one link from seqThen0)
                new Link { FromNodeId = seq.Id,  FromPinId = seqThen0PinId,
                           ToNodeId   = ret1.Id, ToPinId   = ret1ExecInPinId },
                // seq.Then1 → ret2 (one link from seqThen1)
                new Link { FromNodeId = seq.Id,  FromPinId = seqThen1PinId,
                           ToNodeId   = ret2.Id, ToPinId   = ret2ExecInPinId },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "SequenceNodeTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1411);
    }

    /// <summary>
    /// EXEC1-T4: BP1411 includes the exec-out pin name and node id in the message,
    /// enabling the author to locate the offending pin.
    /// </summary>
    [Fact]
    public void Stage2_ExecOutFanOut_DiagnosticIncludesPinAndNodeInfo()
    {
        var (asset, _) = BuildFanOutGraph();

        // Get the entry node id for the assertion
        var entryId = asset.Graphs[0].Nodes.OfType<EventEntryNode>().Single().Id;

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        var d = Assert.Single(sink.All, x => x.Code == DiagnosticCodes.BP1411);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // Message must name the pin and reference the node
        Assert.Contains("Out", d.Message);           // pin name
        Assert.Contains(entryId.ToString(), d.Message); // node id
    }
}

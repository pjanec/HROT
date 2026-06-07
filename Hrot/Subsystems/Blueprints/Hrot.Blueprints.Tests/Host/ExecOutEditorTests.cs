using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for EXEC2 (BF-BATCH-EXECFANOUT): exec-out 1:1 enforcement in the editor.
/// Covers <see cref="BlueprintLinkValidator"/> (exec-out detect + replace signal) and
/// <see cref="BlueprintCommandSink"/> (exec-out replace-on-reconnect + data-input regression).
/// </summary>
public sealed class ExecOutEditorTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintTypeSystem MakeTypeSystem()
        => new(NullPinDefaultValueEditorRegistry.Instance);

    /// <summary>
    /// Builds a three-node exec-only graph:
    ///   NodeA: one exec-out
    ///   NodeB: one exec-in
    ///   NodeC: one exec-in
    /// No links pre-wired (unless caller adds them to the returned graph).
    /// </summary>
    private static (BlueprintAsset asset, Graph graph,
                    Pin execOutA, Pin execInB, Pin execInC)
        BuildExecThreeNodeGraph()
    {
        var execOutAPin = new Pin
            { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var execInBPin  = new Pin
            { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };
        var execInCPin  = new Pin
            { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };

        var nodeA = new EventEntryNode { Id = Guid.NewGuid() };
        nodeA.Pins.Add(execOutAPin);

        var nodeB = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "B" };
        nodeB.Pins.Add(execInBPin);

        var nodeC = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "C" };
        nodeC.Pins.Add(execInCPin);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "TestGraph",
            Kind  = GraphKind.Event,
            Nodes = { nodeA, nodeB, nodeC },
        };

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "ExecOutTest",
            Graphs  = { graph },
        };

        return (asset, graph, execOutAPin, execInBPin, execInCPin);
    }

    private static (BlueprintCommandSink sink, BlueprintGraphModel model)
        MakeSut(BlueprintAsset asset, Graph graph)
    {
        var typeSystem = MakeTypeSystem();
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService
            { Context = new EditServiceContext(history, _ => { }) };
        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, _ => { });
        return (sink, model);
    }

    // ── Validator: exec-out already connected → InvalidReplace ────────────────

    /// <summary>
    /// EXEC2-T1: Validator signals Invalid with an "Exec output" reason when the exec-out
    /// pin already has an outgoing link.
    /// </summary>
    [Fact]
    public void Validator_ExecOut_AlreadyConnected_ReturnsInvalidWithExecOutputReason()
    {
        var (asset, graph, execOutA, execInB, execInC) = BuildExecThreeNodeGraph();

        // Pre-wire execOutA → execInB
        var nodeA = graph.Nodes.OfType<EventEntryNode>().Single();
        var nodeB = graph.Nodes.OfType<FunctionCallNode>().First();
        graph.Links.Add(new Link
        {
            FromNodeId = nodeA.Id, FromPinId = execOutA.Id,
            ToNodeId   = nodeB.Id, ToPinId   = execInB.Id,
        });

        var model     = new BlueprintGraphModel(asset, graph);
        var validator = new BlueprintLinkValidator(model, MakeTypeSystem());

        // Now try wiring execOutA → execInC (output side already connected)
        var result = validator.Validate(new PinId(execOutA.Id), new PinId(execInC.Id));

        Assert.Equal(LinkValidity.Invalid, result.Verdict);
        Assert.NotNull(result.Reason);
        Assert.Contains("Exec output", result.Reason);
    }

    /// <summary>
    /// EXEC2-T2: Validator returns Valid when exec-out has no existing link (fresh connect).
    /// </summary>
    [Fact]
    public void Validator_ExecOut_NoExistingLink_ReturnsValid()
    {
        var (asset, graph, execOutA, execInB, _) = BuildExecThreeNodeGraph();
        // No links pre-wired
        var model     = new BlueprintGraphModel(asset, graph);
        var validator = new BlueprintLinkValidator(model, MakeTypeSystem());

        var result = validator.Validate(new PinId(execOutA.Id), new PinId(execInB.Id));

        Assert.Equal(LinkValidity.Valid, result.Verdict);
    }

    /// <summary>
    /// EXEC2-T3 (regression): Exec-input fan-in is preserved -- adding a second exec-in
    /// connection to a pin that already has one source is still Valid.
    /// </summary>
    [Fact]
    public void Validator_ExecIn_FanInStillAllowed()
    {
        // NodeA exec-out and NodeB exec-out both wiring into NodeC exec-in
        var execOutAPin = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var execOutBPin = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var execInCPin  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };

        var nodeA = new EventEntryNode { Id = Guid.NewGuid() };
        nodeA.Pins.Add(execOutAPin);
        var nodeB = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "B" };
        nodeB.Pins.Add(execOutBPin);
        var nodeC = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "C" };
        nodeC.Pins.Add(execInCPin);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "G",
            Kind  = GraphKind.Event,
            Nodes = { nodeA, nodeB, nodeC },
            // Pre-wire nodeA → nodeC
            Links =
            {
                new Link { FromNodeId = nodeA.Id, FromPinId = execOutAPin.Id,
                           ToNodeId   = nodeC.Id, ToPinId   = execInCPin.Id },
            },
        };

        var asset  = new BlueprintAsset { AssetId = Guid.NewGuid(), Graphs = { graph } };
        var model  = new BlueprintGraphModel(asset, graph);
        var v      = new BlueprintLinkValidator(model, MakeTypeSystem());

        // Adding a second source (nodeB) into execInC is fan-in -- must remain Valid
        var result = v.Validate(new PinId(execOutBPin.Id), new PinId(execInCPin.Id));

        Assert.Equal(LinkValidity.Valid, result.Verdict);
    }

    // ── Sink: exec-out replace-on-reconnect ───────────────────────────────────

    /// <summary>
    /// EXEC2-T4: Applying AddLink for an exec-out pin that already has a link removes the
    /// old link and installs the new one; the graph ends with exactly one link from that
    /// exec-out pin, pointing at the new target.
    /// </summary>
    [Fact]
    public void Sink_AddExecLink_ReplacesExistingExecOutLink()
    {
        var (asset, graph, execOutA, execInB, execInC) = BuildExecThreeNodeGraph();

        var nodeA = graph.Nodes.OfType<EventEntryNode>().Single();
        var nodeB = graph.Nodes.OfType<FunctionCallNode>().First(n => n.MethodName == "B");
        var nodeC = graph.Nodes.OfType<FunctionCallNode>().First(n => n.MethodName == "C");

        // Pre-wire execOutA → execInB (the "old" link)
        graph.Links.Add(new Link
        {
            FromNodeId = nodeA.Id, FromPinId = execOutA.Id,
            ToNodeId   = nodeB.Id, ToPinId   = execInB.Id,
        });
        Assert.Single(graph.Links);

        var (sink, _) = MakeSut(asset, graph);

        // Now wire execOutA → execInC (should replace execOutA → execInB)
        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(execOutA.Id),
            new PinId(execInC.Id)));

        Assert.True(result.Success, result.Message);

        // Exactly one link from execOutA
        var linksFromA = graph.Links.Where(l => l.FromPinId == execOutA.Id).ToList();
        Assert.Single(linksFromA);

        // That link now points to execInC (new target)
        Assert.Equal(execInC.Id, linksFromA[0].ToPinId);
    }

    // ── Sink: data-input replacement regression ────────────────────────────────

    /// <summary>
    /// EXEC2-T5 (regression): The existing data-input single-connection replacement still
    /// removes by To-pin and correctly installs the new data source.
    /// </summary>
    [Fact]
    public void Sink_AddDataLink_DataInputReplacement_StillWorksAfterExecChange()
    {
        var (asset, graph) = MakeAssetWithDataGraph();

        var n1DataOut = graph.Nodes.OfType<FunctionCallNode>().First(n => n.MethodName == "Src1").Pins[0];
        var n2DataOut = graph.Nodes.OfType<FunctionCallNode>().First(n => n.MethodName == "Src2").Pins[0];
        var nDstIn    = graph.Nodes.OfType<FunctionCallNode>().First(n => n.MethodName == "Dst").Pins[0];

        // Pre-wire n1 → dst (data-in)
        graph.Links.Add(new Link
        {
            FromNodeId = graph.Nodes.OfType<FunctionCallNode>().First(n => n.MethodName == "Src1").Id,
            FromPinId  = n1DataOut.Id,
            ToNodeId   = graph.Nodes.OfType<FunctionCallNode>().First(n => n.MethodName == "Dst").Id,
            ToPinId    = nDstIn.Id,
        });

        var (sink, _) = MakeSut(asset, graph);

        // Wire n2 → dst (replaces n1 → dst by To-pin)
        var result = sink.Apply(new GraphCommand.AddLink(
            new LinkId(Guid.NewGuid()),
            new PinId(n2DataOut.Id),
            new PinId(nDstIn.Id)));

        Assert.True(result.Success, result.Message);

        // Exactly one link to nDstIn, sourced from n2
        var linksToDst = graph.Links.Where(l => l.ToPinId == nDstIn.Id).ToList();
        Assert.Single(linksToDst);
        Assert.Equal(n2DataOut.Id, linksToDst[0].FromPinId);
    }

    // ── private test-graph builders ───────────────────────────────────────────

    private static (BlueprintAsset asset, Graph graph) MakeAssetWithDataGraph()
    {
        var n1 = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Src1" };
        n1.Pins.Add(new Pin
        {
            Id = Guid.NewGuid(), Name = "V", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
        });

        var n2 = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Src2" };
        n2.Pins.Add(new Pin
        {
            Id = Guid.NewGuid(), Name = "V", Direction = "Out", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
        });

        var dst = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Dst" };
        dst.Pins.Add(new Pin
        {
            Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
        });

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "DataGraph",
            Kind  = GraphKind.Event,
            Nodes = { n1, n2, dst },
        };

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "DataRegressionAsset",
            Graphs  = { graph },
        };

        return (asset, graph);
    }
}

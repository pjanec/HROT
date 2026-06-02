using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for <see cref="BlueprintLinkValidator"/>.
/// All tests are headless.
/// </summary>
public sealed class BlueprintLinkValidatorTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintTypeSystem MakeTypeSystem()
        => new(NullPinDefaultValueEditorRegistry.Instance);

    /// <summary>
    /// Builds an asset graph that has:
    ///   NodeA: exec-out + data-out(float)
    ///   NodeB: exec-in  + data-in(float)
    ///   NodeC: exec-in  + data-in(int)
    /// No links yet — the validator tests wire things independently.
    /// </summary>
    private static (BlueprintGraphModel model, BlueprintLinkValidator validator,
                    Pin execOutA, Pin dataOutA,
                    Pin execInB,  Pin dataInB,
                    Pin execInC,  Pin dataInC) BuildTwoPinGraph()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var nodeAId  = Guid.NewGuid();
        var nodeBId  = Guid.NewGuid();
        var nodeCId  = Guid.NewGuid();

        var execOutAPin = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var dataOutAPin = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Single } };
        var execInBPin  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var dataInBPin  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Single } };
        var execInCPin  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var dataInCPin  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Int32 } };

        var nodeA = new LiteralNode { Id = nodeAId, TypeId = "System.Single", Pins = { execOutAPin, dataOutAPin } };
        var nodeB = new FunctionCallNode { Id = nodeBId, Pins = { execInBPin, dataInBPin } };
        var nodeC = new FunctionCallNode { Id = nodeCId, Pins = { execInCPin, dataInCPin } };

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "TestGraph",
            Kind  = GraphKind.Function,
            Nodes = { nodeA, nodeB, nodeC },
        };

        var asset = new BlueprintAsset
        {
            AssetId = assetId,
            Name    = "TestAsset",
            Graphs  = { graph },
        };

        var ts    = MakeTypeSystem();
        var model = new BlueprintGraphModel(asset, graph);
        var v     = new BlueprintLinkValidator(model, ts);

        return (model, v,
                execOutAPin, dataOutAPin,
                execInBPin,  dataInBPin,
                execInCPin,  dataInCPin);
    }

    // ── valid connections ─────────────────────────────────────────────────────

    [Fact]
    public void AllowsValidExecLink()
    {
        var (_, v, execOutA, _, execInB, _, _, _) = BuildTwoPinGraph();
        var result = v.Validate(new PinId(execOutA.Id), new PinId(execInB.Id));
        Assert.Equal(LinkValidity.Valid, result.Verdict);
    }

    [Fact]
    public void AllowsValidDataLink_SameType()
    {
        var (_, v, _, dataOutA, _, dataInB, _, _) = BuildTwoPinGraph();
        var result = v.Validate(new PinId(dataOutA.Id), new PinId(dataInB.Id));
        Assert.Equal(LinkValidity.Valid, result.Verdict);
    }

    [Fact]
    public void AllowsDataLink_IntToFloat_ImplicitCast()
    {
        // Build a graph where output is Int32 and input is Single.
        var nodeAId  = Guid.NewGuid();
        var nodeBId  = Guid.NewGuid();
        var intOutPin   = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Int32  } };
        var floatInPin  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Single } };

        var nodeA = new GetVariableNode { Id = nodeAId, Pins = { intOutPin } };
        var nodeB = new FunctionCallNode { Id = nodeBId, Pins = { floatInPin } };

        var graph = new Graph { Id = Guid.NewGuid(), Name = "G", Kind = GraphKind.Function, Nodes = { nodeA, nodeB } };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Graphs = { graph } };
        var ts    = MakeTypeSystem();
        var model = new BlueprintGraphModel(asset, graph);
        var v     = new BlueprintLinkValidator(model, ts);

        var result = v.Validate(new PinId(intOutPin.Id), new PinId(floatInPin.Id));
        Assert.Equal(LinkValidity.Valid, result.Verdict);
    }

    // ── incompatible types ────────────────────────────────────────────────────

    [Fact]
    public void RejectsIncompatibleDataTypes()
    {
        var (_, v, _, dataOutA, _, _, _, dataInC) = BuildTwoPinGraph();
        // dataOutA is float, dataInC is int — float→int is not allowed
        var result = v.Validate(new PinId(dataOutA.Id), new PinId(dataInC.Id));
        Assert.Equal(LinkValidity.Invalid, result.Verdict);
        Assert.NotNull(result.Reason);
    }

    // ── exec ↔ data mixing ────────────────────────────────────────────────────

    [Fact]
    public void RejectsExecToData()
    {
        var (_, v, execOutA, _, _, dataInB, _, _) = BuildTwoPinGraph();
        var result = v.Validate(new PinId(execOutA.Id), new PinId(dataInB.Id));
        Assert.Equal(LinkValidity.Invalid, result.Verdict);
    }

    [Fact]
    public void RejectsDataToExec()
    {
        var (_, v, _, dataOutA, execInB, _, _, _) = BuildTwoPinGraph();
        var result = v.Validate(new PinId(dataOutA.Id), new PinId(execInB.Id));
        Assert.Equal(LinkValidity.Invalid, result.Verdict);
    }

    // ── direction ─────────────────────────────────────────────────────────────

    [Fact]
    public void RejectsSameDirection_OutputToOutput()
    {
        // Build two output data pins.
        var nodeAId = Guid.NewGuid();
        var nodeBId = Guid.NewGuid();
        var outA = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Single } };
        var outB = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Single } };

        var graph = new Graph { Id = Guid.NewGuid(), Name = "G", Kind = GraphKind.Function,
            Nodes = {
                new LiteralNode { Id = nodeAId, TypeId = "System.Single", Pins = { outA } },
                new LiteralNode { Id = nodeBId, TypeId = "System.Single", Pins = { outB } },
            }
        };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Graphs = { graph } };
        var model = new BlueprintGraphModel(asset, graph);
        var v     = new BlueprintLinkValidator(model, MakeTypeSystem());

        var result = v.Validate(new PinId(outA.Id), new PinId(outB.Id));
        Assert.Equal(LinkValidity.Invalid, result.Verdict);
    }

    // ── self-loop ─────────────────────────────────────────────────────────────

    [Fact]
    public void RejectsSelfLoop_SamePinId()
    {
        var (_, v, execOutA, _, _, _, _, _) = BuildTwoPinGraph();
        var result = v.Validate(new PinId(execOutA.Id), new PinId(execOutA.Id));
        Assert.Equal(LinkValidity.Invalid, result.Verdict);
    }

    // ── pin not found ─────────────────────────────────────────────────────────

    [Fact]
    public void RejectsUnknownPin()
    {
        var (_, v, _, _, _, _, _, _) = BuildTwoPinGraph();
        var result = v.Validate(new PinId(Guid.NewGuid()), new PinId(Guid.NewGuid()));
        Assert.Equal(LinkValidity.Invalid, result.Verdict);
    }

    // ── single data-input rule ────────────────────────────────────────────────

    [Fact]
    public void SingleDataInput_SecondConnection_IsRejected()
    {
        var (model, v, _, dataOutA, _, dataInB, _, _) = BuildTwoPinGraph();

        // First link is valid.
        var first = v.Validate(new PinId(dataOutA.Id), new PinId(dataInB.Id));
        Assert.Equal(LinkValidity.Valid, first.Verdict);

        // Simulate the link already existing by adding it to the asset graph.
        var assetGraph = model.Nodes.First(n =>
            n.Pins.Any(p => p.Id.Value == dataInB.Id));
        // Add link to asset directly to reflect existing connection.
        // The model only reads its graph's Links, so add it there.
        // We need to reach the graph — create a second float output pin on a new node.
        var nodeD = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Single",
            Pins = { new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out",
                IsExec = false, TypeRef = new() { TypeId = BlueprintTypeSystem.Single } } }
        };

        // Add a link to the graph (simulating first connection already present).
        var graph2Graph = new Hrot.Blueprints.Core.Assets.Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "G2",
            Kind  = GraphKind.Function,
            Nodes =
            {
                new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Single",
                    Pins = { dataOutA } },
                new FunctionCallNode { Id = Guid.NewGuid(),
                    Pins = { dataInB } },
                nodeD,
            },
            Links =
            {
                // Existing link: dataOutA → dataInB
                new Link { FromNodeId = Guid.NewGuid(), FromPinId = dataOutA.Id,
                           ToNodeId   = Guid.NewGuid(), ToPinId   = dataInB.Id },
            }
        };
        var asset2 = new BlueprintAsset { AssetId = Guid.NewGuid(), Graphs = { graph2Graph } };
        var model2 = new BlueprintGraphModel(asset2, graph2Graph);
        var v2     = new BlueprintLinkValidator(model2, MakeTypeSystem());

        // Now try to add another data link to the same input pin — should be rejected.
        var second = v2.Validate(new PinId(nodeD.Pins[0].Id), new PinId(dataInB.Id));
        Assert.Equal(LinkValidity.Invalid, second.Verdict);
    }
}

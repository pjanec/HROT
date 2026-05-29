using System.Reflection;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Tests;

public sealed class SchemaReflectionTests
{
    [Fact]
    public void ConcreteNodeSubtypeCount_Is24()
    {
        var count = typeof(Node).Assembly
            .GetTypes()
            .Count(t => !t.IsAbstract && t.IsSubclassOf(typeof(Node)));

        Assert.Equal(24, count);
    }

    [Theory]
    [InlineData(typeof(FunctionCallNode),        "FunctionCall")]
    [InlineData(typeof(BranchNode),              "Branch")]
    [InlineData(typeof(SequenceNode),            "Sequence")]
    [InlineData(typeof(GetVariableNode),         "GetVariable")]
    [InlineData(typeof(SetVariableNode),         "SetVariable")]
    [InlineData(typeof(LiteralNode),             "Literal")]
    [InlineData(typeof(EventEntryNode),          "EventEntry")]
    [InlineData(typeof(ReturnNode),              "Return")]
    [InlineData(typeof(CastNode),                "Cast")]
    [InlineData(typeof(ArrayMakeNode),           "ArrayMake")]
    [InlineData(typeof(ArrayGetNode),            "ArrayGet")]
    [InlineData(typeof(LatentDelayNode),         "Delay")]
    [InlineData(typeof(CallEventDispatcherNode), "CallDispatcher")]
    [InlineData(typeof(BindEventDispatcherNode), "BindDispatcher")]
    [InlineData(typeof(CallCustomEventNode),     "CallCustomEvent")]
    [InlineData(typeof(CallPeerBlueprintNode),   "CallPeerBlueprint")]
    [InlineData(typeof(ChannelCommandNode),      "ChannelCommand")]
    [InlineData(typeof(WaitForChannelNode),      "WaitForChannel")]
    [InlineData(typeof(WaitForEventNode),        "WaitForEvent")]
    [InlineData(typeof(WhenNode),           "When")]
    [InlineData(typeof(ReadEqsResultNode),  "ReadEqsResult")]
    [InlineData(typeof(SpawnEqsSensorNode), "SpawnEqsSensor")]
    public void DiscriminatorRoundTrip_EachNodeKind(Type nodeType, string expectedDiscriminator)
    {
        var node = (Node)Activator.CreateInstance(nodeType)!;
        node.Id = Guid.NewGuid();

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "Test",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id    = Guid.NewGuid(),
                    Name  = "Main",
                    Kind  = GraphKind.Function,
                    Nodes = [node],
                },
            ],
        };

        var json = BlueprintJsonServices.Serialize(asset);
        Assert.Contains($"\"kind\":\"{expectedDiscriminator}\"", json);

        var deserialized = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.IsType(nodeType, deserialized.Graphs[0].Nodes[0]);
    }

    [Fact]
    public void UnknownFieldsTolerance_DoesNotThrow()
    {
        const string json = """
            {
                "Name":"Test",
                "Dispatch":"Library",
                "AssetId":"00000000-0000-0000-0000-000000000001",
                "unknownField":"ignored",
                "Graphs":[]
            }
            """;

        var asset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(asset);
        Assert.Equal("Test", asset.Name);
        Assert.Equal(BlueprintDispatchKind.Library, asset.Dispatch);
    }

    [Fact]
    public void MissingFieldsDefaultToEmpty()
    {
        const string json = """
            {
                "Name":"Y",
                "Dispatch":"Instance",
                "AssetId":"00000000-0000-0000-0000-000000000002"
            }
            """;

        var asset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(asset);
        Assert.NotNull(asset.Variables);
        Assert.Empty(asset.Variables);
        Assert.NotNull(asset.Graphs);
        Assert.Empty(asset.Graphs);
        Assert.NotNull(asset.EventDispatchers);
        Assert.Empty(asset.EventDispatchers);
    }

    [Fact]
    public void EqsSensorHandle_IsPermittedVariableType()
    {
        var typeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" };
        bool resolved = StaticTypeRegistry.Instance.TryResolve(typeRef, out var irType);

        Assert.True(resolved);
        Assert.Equal("FDP.Eqs.EqsSensorHandle", irType.FullName);
        Assert.True(irType.IsUnmanaged);
        Assert.Equal(8, irType.SizeBytes);
    }
}

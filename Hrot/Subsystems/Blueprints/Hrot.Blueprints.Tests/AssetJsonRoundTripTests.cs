using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests;

public sealed class AssetJsonRoundTripTests
{
    // SC2: Library dispatch round-trip.
    [Fact]
    public void LibraryDispatch_RoundTrip()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
            Name     = "MathLibrary",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id    = new Guid("a1b2c3d4-0002-0002-0002-000000000002"),
                    Name  = "Abs",
                    Kind  = GraphKind.Function,
                    Nodes =
                    [
                        new FunctionCallNode
                        {
                            Id           = new Guid("a1b2c3d4-0003-0003-0003-000000000003"),
                            TargetTypeId = "System.Math",
                            MethodName   = "Abs",
                            IsPure       = true,
                        },
                    ],
                },
                new Graph
                {
                    Id    = new Guid("a1b2c3d4-0004-0004-0004-000000000004"),
                    Name  = "Pow",
                    Kind  = GraphKind.Function,
                    Nodes =
                    [
                        new FunctionCallNode
                        {
                            Id           = new Guid("a1b2c3d4-0005-0005-0005-000000000005"),
                            TargetTypeId = "System.Math",
                            MethodName   = "Pow",
                            IsPure       = true,
                        },
                    ],
                },
            ],
        };

        var j1          = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(j1);
        Assert.NotNull(deserialized);
        var j2 = BlueprintJsonServices.Serialize(deserialized);

        Assert.Equal(j1, j2);
    }

    // SC3: AiPrimitive dispatch round-trip (exercises AiPrimitiveDecl, Parameters,
    //       WorkingState, ChannelCommandNode, WaitForChannelNode, WaitForEventNode).
    [Fact]
    public void AiPrimitive_RoundTrip()
    {
        var asset = new BlueprintAsset
        {
            AssetId   = new Guid("b1b2c3d4-0001-0001-0001-000000000001"),
            Name      = "MoveToAndFire",
            Dispatch  = BlueprintDispatchKind.AiPrimitive,
            Primitive = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = [AiPrimitiveHosting.BTreeAction, AiPrimitiveHosting.HsmAction],
            },
            Parameters =
            [
                new ParameterDecl
                {
                    Id   = new Guid("b1b2c3d4-0002-0002-0002-000000000002"),
                    Name = "Target",
                    Type = new BlueprintTypeRef { TypeId = "System.Numerics.Vector3" },
                },
            ],
            WorkingState =
            [
                new VariableDecl
                {
                    Id   = new Guid("b1b2c3d4-0003-0003-0003-000000000003"),
                    Name = "Phase",
                    Type = new BlueprintTypeRef { TypeId = "System.Byte" },
                },
            ],
            Graphs =
            [
                new Graph
                {
                    Id    = new Guid("b1b2c3d4-0004-0004-0004-000000000004"),
                    Name  = "Main",
                    Kind  = GraphKind.Function,
                    Nodes =
                    [
                        new ChannelCommandNode
                        {
                            Id          = new Guid("b1b2c3d4-0005-0005-0005-000000000005"),
                            ChannelType = "LocomotionChannel",
                            ActionId    = "ActionIdMoveTo",
                        },
                        new WaitForChannelNode
                        {
                            Id          = new Guid("b1b2c3d4-0006-0006-0006-000000000006"),
                            ChannelType = "LocomotionChannel",
                        },
                        new WaitForEventNode
                        {
                            Id           = new Guid("b1b2c3d4-0007-0007-0007-000000000007"),
                            EventTypeId  = "Hrot.Events.TargetLostEvent",
                            FilterByField = "Target",
                        },
                    ],
                },
            ],
        };

        var j1          = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(j1);
        Assert.NotNull(deserialized);
        var j2 = BlueprintJsonServices.Serialize(deserialized);

        Assert.Equal(j1, j2);
    }

    // SC4: Instance dispatch round-trip.
    [Fact]
    public void Instance_RoundTrip()
    {
        var asset = new BlueprintAsset
        {
            AssetId   = new Guid("c1b2c3d4-0001-0001-0001-000000000001"),
            Name      = "Door",
            Dispatch  = BlueprintDispatchKind.Instance,
            TierHint  = BlackboardTierHint.Auto,
            Variables =
            [
                new VariableDecl
                {
                    Id         = new Guid("c1b2c3d4-0002-0002-0002-000000000002"),
                    Name       = "IsOpen",
                    Type       = new BlueprintTypeRef { TypeId = "System.Boolean" },
                    IsEditable = true,
                },
            ],
            Graphs =
            [
                new Graph
                {
                    Id    = new Guid("c1b2c3d4-0003-0003-0003-000000000003"),
                    Name  = "Toggle",
                    Kind  = GraphKind.Function,
                    Nodes =
                    [
                        new GetVariableNode
                        {
                            Id         = new Guid("c1b2c3d4-0004-0004-0004-000000000004"),
                            VariableId = "c1b2c3d4-0002-0002-0002-000000000002",
                        },
                    ],
                },
                new Graph
                {
                    Id    = new Guid("c1b2c3d4-0005-0005-0005-000000000005"),
                    Name  = "OnInteract",
                    Kind  = GraphKind.Event,
                    Nodes =
                    [
                        new EventEntryNode
                        {
                            Id          = new Guid("c1b2c3d4-0006-0006-0006-000000000006"),
                            EventTypeId = "Hrot.Events.InteractEvent",
                        },
                    ],
                },
            ],
        };

        var j1          = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(j1);
        Assert.NotNull(deserialized);
        var j2 = BlueprintJsonServices.Serialize(deserialized);

        Assert.Equal(j1, j2);
    }

    // SC5: Polymorphic node coverage -- all 19 concrete subtypes in one graph.
    [Fact]
    public void AllNodeTypes_PolymorphicRoundTrip()
    {
        Type[] expectedTypes =
        [
            typeof(FunctionCallNode),
            typeof(BranchNode),
            typeof(SequenceNode),
            typeof(GetVariableNode),
            typeof(SetVariableNode),
            typeof(LiteralNode),
            typeof(EventEntryNode),
            typeof(ReturnNode),
            typeof(CastNode),
            typeof(ArrayMakeNode),
            typeof(ArrayGetNode),
            typeof(LatentDelayNode),
            typeof(CallEventDispatcherNode),
            typeof(BindEventDispatcherNode),
            typeof(CallCustomEventNode),
            typeof(CallPeerBlueprintNode),
            typeof(ChannelCommandNode),
            typeof(WaitForChannelNode),
            typeof(WaitForEventNode),
        ];

        var nodes = new List<Node>
        {
            new FunctionCallNode        { Id = new Guid("d0000000-0000-0000-0000-000000000001") },
            new BranchNode              { Id = new Guid("d0000000-0000-0000-0000-000000000002") },
            new SequenceNode            { Id = new Guid("d0000000-0000-0000-0000-000000000003") },
            new GetVariableNode         { Id = new Guid("d0000000-0000-0000-0000-000000000004") },
            new SetVariableNode         { Id = new Guid("d0000000-0000-0000-0000-000000000005") },
            new LiteralNode             { Id = new Guid("d0000000-0000-0000-0000-000000000006") },
            new EventEntryNode          { Id = new Guid("d0000000-0000-0000-0000-000000000007") },
            new ReturnNode              { Id = new Guid("d0000000-0000-0000-0000-000000000008") },
            new CastNode                { Id = new Guid("d0000000-0000-0000-0000-000000000009") },
            new ArrayMakeNode           { Id = new Guid("d0000000-0000-0000-0000-00000000000a") },
            new ArrayGetNode            { Id = new Guid("d0000000-0000-0000-0000-00000000000b") },
            new LatentDelayNode         { Id = new Guid("d0000000-0000-0000-0000-00000000000c") },
            new CallEventDispatcherNode { Id = new Guid("d0000000-0000-0000-0000-00000000000d") },
            new BindEventDispatcherNode { Id = new Guid("d0000000-0000-0000-0000-00000000000e") },
            new CallCustomEventNode     { Id = new Guid("d0000000-0000-0000-0000-00000000000f") },
            new CallPeerBlueprintNode   { Id = new Guid("d0000000-0000-0000-0000-000000000010") },
            new ChannelCommandNode      { Id = new Guid("d0000000-0000-0000-0000-000000000011") },
            new WaitForChannelNode      { Id = new Guid("d0000000-0000-0000-0000-000000000012") },
            new WaitForEventNode        { Id = new Guid("d0000000-0000-0000-0000-000000000013") },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("d0000000-0000-0000-0000-000000000000"),
            Name     = "AllNodes",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id    = new Guid("d0000000-0000-0000-0000-000000000099"),
                    Name  = "AllNodeGraph",
                    Kind  = GraphKind.Function,
                    Nodes = nodes,
                },
            ],
        };

        var j1          = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(j1);
        Assert.NotNull(deserialized);
        var j2 = BlueprintJsonServices.Serialize(deserialized);

        Assert.Equal(j1, j2);

        var deserializedNodes = deserialized.Graphs[0].Nodes;
        Assert.Equal(19, deserializedNodes.Count);
        for (int i = 0; i < expectedTypes.Length; i++)
            Assert.IsType(expectedTypes[i], deserializedNodes[i]);
    }

    // SC6: Unknown fields at top level and inside a Node are silently ignored.
    [Fact]
    public void UnknownField_Tolerated()
    {
        const string json = """
            {"Name":"X","Dispatch":"Library","AssetId":"00000000-0000-0000-0000-000000000001","unknownField":"ignored","Graphs":[]}
            """;

        var asset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(asset);
        Assert.Equal("X", asset.Name);
        Assert.Equal(BlueprintDispatchKind.Library, asset.Dispatch);
    }

    // SC7: Missing list fields produce non-null empty lists.
    [Fact]
    public void MissingFields_DefaultToEmpty()
    {
        const string json = """
            {"Name":"Y","Dispatch":"Instance","AssetId":"00000000-0000-0000-0000-000000000002"}
            """;

        var asset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(asset);
        Assert.NotNull(asset.Variables);
        Assert.Empty(asset.Variables);
        Assert.NotNull(asset.Graphs);
        Assert.Empty(asset.Graphs);
        Assert.NotNull(asset.EventDispatchers);
        Assert.Empty(asset.EventDispatchers);
        Assert.NotNull(asset.Parameters);
        Assert.Empty(asset.Parameters);
        Assert.NotNull(asset.WorkingState);
        Assert.Empty(asset.WorkingState);
        Assert.NotNull(asset.CustomEvents);
        Assert.Empty(asset.CustomEvents);
        Assert.NotNull(asset.CallablePeers);
        Assert.Empty(asset.CallablePeers);
    }
}

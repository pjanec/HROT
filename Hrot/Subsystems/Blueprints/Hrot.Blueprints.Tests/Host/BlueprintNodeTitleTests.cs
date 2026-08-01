using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Editor punch-list #1/#5/#8: a node's canvas body must surface its own data (literal value,
/// parameter name, compare/arith/bool operator) instead of the generic "Value" pin label. The title
/// is <see cref="BlueprintNodeModel"/>'s single body string, so these assert it directly.
/// </summary>
public sealed class BlueprintNodeTitleTests
{
    private static string Title(Node node, BlueprintAsset? asset = null)
        => new BlueprintNodeModel(node, System.Array.Empty<IPinModel>(), asset).Title;

    private static NodeState State(Node node, BlueprintAsset? asset = null)
        => new BlueprintNodeModel(node, System.Array.Empty<IPinModel>(), asset).State;

    [Theory]
    [InlineData("System.Int32", "5")]
    [InlineData("System.Boolean", "true")]
    [InlineData("System.Single", "1.5f")]
    [InlineData("System.String", "\"hello\"")]
    public void Literal_InlineEditableType_TitleIsType(string typeId, string valueJson)
    {
        // Inline-editable literals show their value in the body editor, so the title stays the type.
        var expected = $"Literal ({typeId["System.".Length..]})";
        Assert.Equal(expected, Title(new LiteralNode { TypeId = typeId, ValueJson = valueJson }));
    }

    [Fact]
    public void Literal_Empty_FallsBackToType()
        => Assert.Equal("Literal (Single)", Title(new LiteralNode { TypeId = "System.Single", ValueJson = "" }));

    [Theory]
    [InlineData(ComparisonOperator.Equal, "Compare ==")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, "Compare >=")]
    [InlineData(ComparisonOperator.LessThan, "Compare <")]
    public void Compare_ShowsOperator(ComparisonOperator op, string expected)
        => Assert.Equal(expected, Title(new CompareNode { Operator = op }));

    [Theory]
    [InlineData(ArithmeticOperator.Add, "Math +")]
    [InlineData(ArithmeticOperator.Modulo, "Math %")]
    public void BinaryOp_ShowsOperator(ArithmeticOperator op, string expected)
        => Assert.Equal(expected, Title(new BinaryOpNode { Operator = op }));

    [Theory]
    [InlineData(BooleanOperator.And, "Logic &&")]
    [InlineData(BooleanOperator.Or, "Logic ||")]
    public void BooleanOp_ShowsOperator(BooleanOperator op, string expected)
        => Assert.Equal(expected, Title(new BooleanOpNode { Operator = op }));

    // Q#14 Option B: struct-value node headers show the short struct name (namespace + global::
    // stripped) in [brackets], so the canvas reads "Make [StructDemoData]" — not the raw class name
    // "MakeStructNode", and the struct name is instantly distinguishable from the verb.
    [Theory]
    [InlineData("Hrot.AI.Behaviors.StructDemoData", "Make [StructDemoData]")]
    [InlineData("global::Hrot.AI.Behaviors.StructDemoData", "Make [StructDemoData]")]
    public void MakeStruct_ShowsShortStructName(string fqn, string expected)
        => Assert.Equal(expected, Title(new MakeStructNode { StructTypeId = fqn }));

    [Fact]
    public void BreakStruct_ShowsShortStructName()
        => Assert.Equal("Break [StructDemoData]",
            Title(new BreakStructNode { StructTypeId = "Hrot.AI.Behaviors.StructDemoData" }));

    [Fact]
    public void SetMembers_ShowsShortStructName()
        => Assert.Equal("Set Members [StructDemoData]",
            Title(new SetMembersNode { StructTypeId = "Hrot.AI.Behaviors.StructDemoData" }));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void StructNodes_EmptyType_FallBackToGenericLabel(string? fqn)
    {
        Assert.Equal("Make Struct",  Title(new MakeStructNode  { StructTypeId = fqn! }));
        Assert.Equal("Break Struct", Title(new BreakStructNode { StructTypeId = fqn! }));
        Assert.Equal("Set Members",  Title(new SetMembersNode  { StructTypeId = fqn! }));
    }

    // Get/Set Shared bracket the slot name into the title; empty slot keeps the bare verb.
    [Fact]
    public void GetShared_BracketsSlotName()
        => Assert.Equal("Get Shared [RallyPoint]", Title(new GetSharedNode { VariableId = "RallyPoint" }));

    [Fact]
    public void SetShared_BracketsSlotName()
        => Assert.Equal("Set Shared [RallyPoint]", Title(new SetSharedNode { VariableId = "RallyPoint" }));

    [Fact]
    public void Shared_EmptySlot_KeepsBareTitle()
    {
        Assert.Equal("Get Shared", Title(new GetSharedNode { VariableId = "" }));
        Assert.Equal("Set Shared", Title(new SetSharedNode { VariableId = "" }));
    }

    // CA-02: GetComponent brackets the short component-type name (mirrors Make/Break/SetMembers'
    // "[ShortTypeName]" convention), and flags NodeState.Error when the baked ComponentTypeFqn no
    // longer resolves (renamed/removed from C#) -- reuses the FunctionCall red-node pattern.

    [Fact]
    public void GetComponent_BracketsShortComponentTypeName()
        => Assert.Equal("Get Component [Vector3]",
            Title(new GetComponentNode { ComponentTypeFqn = "System.Numerics.Vector3" }));

    [Fact]
    public void GetComponent_EmptyComponentType_FallsBackToGenericLabel()
        => Assert.Equal("Get Component", Title(new GetComponentNode { ComponentTypeFqn = "" }));

    [Fact]
    public void GetComponent_ResolvableComponentType_IsNormalState()
    {
        var node = new GetComponentNode { ComponentTypeFqn = "System.Numerics.Vector3" };
        Assert.Equal(NodeState.Normal, State(node));
    }

    [Fact]
    public void GetComponent_UnresolvableComponentType_IsErrorState()
    {
        var node = new GetComponentNode { ComponentTypeFqn = "Totally.Unknown.Namespace.NoSuchComponent" };
        Assert.Equal(NodeState.Error, State(node));
    }

    [Fact]
    public void GetComponent_EmptyComponentType_IsNormalState_NotError()
    {
        // An unconfigured (not-yet-picked) node is not the same as a STALE reference -- must not
        // be flagged as an error just because ComponentTypeFqn happens to be empty.
        var node = new GetComponentNode { ComponentTypeFqn = "" };
        Assert.Equal(NodeState.Normal, State(node));
    }

    // CA-04: SetComponent mirrors GetComponent's title/stale-ref conventions exactly (same
    // "[ShortTypeName]" bracketing, same red-node-on-unresolved-component pattern).

    [Fact]
    public void SetComponent_BracketsShortComponentTypeName()
        => Assert.Equal("Set Component [Vector3]",
            Title(new SetComponentNode { ComponentTypeFqn = "System.Numerics.Vector3" }));

    [Fact]
    public void SetComponent_EmptyComponentType_FallsBackToGenericLabel()
        => Assert.Equal("Set Component", Title(new SetComponentNode { ComponentTypeFqn = "" }));

    [Fact]
    public void SetComponent_ResolvableComponentType_IsNormalState()
    {
        var node = new SetComponentNode { ComponentTypeFqn = "System.Numerics.Vector3" };
        Assert.Equal(NodeState.Normal, State(node));
    }

    [Fact]
    public void SetComponent_UnresolvableComponentType_IsErrorState()
    {
        var node = new SetComponentNode { ComponentTypeFqn = "Totally.Unknown.Namespace.NoSuchComponent" };
        Assert.Equal(NodeState.Error, State(node));
    }

    [Fact]
    public void SetComponent_EmptyComponentType_IsNormalState_NotError()
    {
        // An unconfigured (not-yet-picked) node is not the same as a STALE reference -- must not
        // be flagged as an error just because ComponentTypeFqn happens to be empty.
        var node = new SetComponentNode { ComponentTypeFqn = "" };
        Assert.Equal(NodeState.Normal, State(node));
    }

    // CA-07c: the three collection CONSUMER nodes bracket the short component-type name once baked
    // (on wire), same convention as GetComponent/SetComponent; a fresh/unwired instance shows a
    // generic label instead.

    [Fact]
    public void ComponentForEach_BracketsShortComponentTypeName()
        => Assert.Equal("For Each [BpCollectionDemo]",
            Title(new ComponentForEachNode { ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo" }));

    [Fact]
    public void ComponentForEach_EmptyComponentType_FallsBackToGenericLabel()
        => Assert.Equal("For Each Component Item", Title(new ComponentForEachNode { ComponentTypeFqn = "" }));

    [Fact]
    public void ComponentItemGet_BracketsShortComponentTypeName()
        => Assert.Equal("Get Item [BpCollectionDemo]",
            Title(new ComponentItemGetNode { ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo" }));

    [Fact]
    public void ComponentItemGet_EmptyComponentType_FallsBackToGenericLabel()
        => Assert.Equal("Get Item", Title(new ComponentItemGetNode { ComponentTypeFqn = "" }));

    [Fact]
    public void ComponentItemCount_BracketsShortComponentTypeName()
        => Assert.Equal("Item Count [BpCollectionDemo]",
            Title(new ComponentItemCountNode { ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo" }));

    [Fact]
    public void ComponentItemCount_EmptyComponentType_FallsBackToGenericLabel()
        => Assert.Equal("Item Count", Title(new ComponentItemCountNode { ComponentTypeFqn = "" }));

    [Fact]
    public void ComponentCollectionConsumers_ResolvableComponentType_IsNormalState()
    {
        Assert.Equal(NodeState.Normal, State(new ComponentForEachNode   { ComponentTypeFqn = "System.Numerics.Vector3" }));
        Assert.Equal(NodeState.Normal, State(new ComponentItemGetNode   { ComponentTypeFqn = "System.Numerics.Vector3" }));
        Assert.Equal(NodeState.Normal, State(new ComponentItemCountNode { ComponentTypeFqn = "System.Numerics.Vector3" }));
    }

    [Fact]
    public void ComponentCollectionConsumers_UnresolvableComponentType_IsErrorState()
    {
        const string bogus = "Totally.Unknown.Namespace.NoSuchComponent";
        Assert.Equal(NodeState.Error, State(new ComponentForEachNode   { ComponentTypeFqn = bogus }));
        Assert.Equal(NodeState.Error, State(new ComponentItemGetNode   { ComponentTypeFqn = bogus }));
        Assert.Equal(NodeState.Error, State(new ComponentItemCountNode { ComponentTypeFqn = bogus }));
    }

    [Fact]
    public void ComponentCollectionConsumers_EmptyComponentType_IsNormalState_NotError()
    {
        // Not-yet-wired (freshly placed from the palette) is not the same as a STALE reference.
        Assert.Equal(NodeState.Normal, State(new ComponentForEachNode   { ComponentTypeFqn = "" }));
        Assert.Equal(NodeState.Normal, State(new ComponentItemGetNode   { ComponentTypeFqn = "" }));
        Assert.Equal(NodeState.Normal, State(new ComponentItemCountNode { ComponentTypeFqn = "" }));
    }

    // CA-07c: BP2066-mirroring check -- "Collection" wired but baked accessors empty (the
    // "collectionPinWired" signal BlueprintGraphModel computes from _graph.Links; these tests drive
    // it directly since BlueprintNodeModel itself has no connectivity awareness).

    [Fact]
    public void ComponentCollectionConsumer_WiredButAccessorsEmpty_IsErrorState_MirrorsBP2066()
    {
        var node = new ComponentItemCountNode { ComponentTypeFqn = "", CountAccessorFqn = "" };
        var model = new BlueprintNodeModel(node, System.Array.Empty<IPinModel>(), asset: null, collectionPinWired: true);
        Assert.Equal(NodeState.Error, model.State);
    }

    [Fact]
    public void ComponentCollectionConsumer_UnwiredAndAccessorsEmpty_IsNormalState_NotBP2066()
    {
        // Unwired ("not used yet") is a legitimate state -- mirrors Stage2's own "only fires when
        // wired" rule -- so the SAME empty-accessors node must NOT be flagged when unwired.
        var node = new ComponentItemCountNode { ComponentTypeFqn = "", CountAccessorFqn = "" };
        var model = new BlueprintNodeModel(node, System.Array.Empty<IPinModel>(), asset: null, collectionPinWired: false);
        Assert.Equal(NodeState.Normal, model.State);
    }

    [Fact]
    public void ComponentCollectionConsumer_WiredWithFullBake_IsNormalState()
    {
        // "System.Numerics.Vector3" (not a Hrot.AI.Behaviors type) so the stale-ref check's
        // ComponentFieldReflector.ResolveType finds it via plain reflection in THIS test host,
        // mirroring GetComponent_ResolvableComponentType_IsNormalState above.
        var node = new ComponentForEachNode
        {
            ComponentTypeFqn = "System.Numerics.Vector3",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "System.Int32",
        };
        var model = new BlueprintNodeModel(node, System.Array.Empty<IPinModel>(), asset: null, collectionPinWired: true);
        Assert.Equal(NodeState.Normal, model.State);
    }

    [Fact]
    public void GetParameter_TitleIsClean_NameShownOnPinInstead()
    {
        // The parameter NAME now labels the output pin (render-only, in BlueprintGraphModel), so the
        // node title stays generic and uncluttered.
        var pid = System.Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            AssetId = System.Guid.NewGuid(),
            Name = "T",
            Parameters = new() { new ParameterDecl { Id = pid, Name = "FiringLineStart" } },
        };
        Assert.Equal("Get Parameter", Title(new GetParameterNode { ParameterId = pid.ToString() }, asset));
    }
}

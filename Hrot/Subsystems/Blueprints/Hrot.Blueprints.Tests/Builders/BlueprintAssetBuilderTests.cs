using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Builders;

/// <summary>
/// Tests for BlueprintAssetBuilder fluent API (TH-004 SC1-SC6).
/// </summary>
public sealed class BlueprintAssetBuilderTests
{
    // SC1: Library factory -- correct dispatch, empty lists.
    [Fact]
    public void Library_Build_CorrectDispatchAndEmptyLists()
    {
        var asset = BlueprintAssetBuilder.Library("Foo").Build();

        Assert.Equal(BlueprintDispatchKind.Library, asset.Dispatch);
        Assert.Equal("Foo", asset.Name);
        Assert.NotNull(asset.Graphs);
        Assert.Empty(asset.Graphs);
        Assert.NotNull(asset.Variables);
        Assert.Empty(asset.Variables);
        Assert.NotNull(asset.Parameters);
        Assert.Empty(asset.Parameters);
        Assert.NotNull(asset.WorkingState);
        Assert.Empty(asset.WorkingState);
        Assert.NotNull(asset.CustomEvents);
        Assert.Empty(asset.CustomEvents);
        Assert.NotNull(asset.EventDispatchers);
        Assert.Empty(asset.EventDispatchers);
        Assert.NotNull(asset.CallablePeers);
        Assert.Empty(asset.CallablePeers);
    }

    // SC2: AiPrimitive with all decorations.
    [Fact]
    public void AiPrimitive_WithAllDecorations_CorrectCounts()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("HasTarget")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithParameter("Threshold", typeof(float))
            .WithWorkingStateField("Phase", typeof(int))
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Success))
            .Build();

        Assert.Equal(BlueprintDispatchKind.AiPrimitive, asset.Dispatch);
        Assert.NotNull(asset.Primitive);
        Assert.Equal(AiPrimitiveIntent.Condition, asset.Primitive!.Intent);
        Assert.Equal(1, asset.Primitive.Hostings.Count);
        Assert.Equal(1, asset.Parameters.Count);
        Assert.Equal(1, asset.WorkingState.Count);
        Assert.Equal(1, asset.Graphs.Count);
        Assert.Equal(2, asset.Graphs[0].Nodes.Count);
        Assert.Equal(1, asset.Graphs[0].Links.Count);
    }

    // SC3: Determinism -- same builder sequence twice produces identical JSON.
    [Fact]
    public void Build_SameBuilderSequenceTwice_ProducesIdenticalAssets()
    {
        var asset1 = BlueprintAssetBuilder
            .Library("MathLib")
            .WithGraph("Abs", g => g.Entry().Return())
            .Build();

        var asset2 = BlueprintAssetBuilder
            .Library("MathLib")
            .WithGraph("Abs", g => g.Entry().Return())
            .Build();

        var json1 = BlueprintJsonServices.Serialize(asset1);
        var json2 = BlueprintJsonServices.Serialize(asset2);

        Assert.Equal(json1, json2);
    }

    // SC4: Instance with variable and custom event.
    [Fact]
    public void Instance_WithVariableAndCustomEvent_CorrectCounts()
    {
        var asset = BlueprintAssetBuilder
            .Instance("Door")
            .WithVariable("HP", typeof(int))
            .WithCustomEvent("OnHit", ("Damage", typeof(int)))
            .Build();

        Assert.Equal(BlueprintDispatchKind.Instance, asset.Dispatch);
        Assert.Equal(1, asset.Variables.Count);
        Assert.Equal("HP", asset.Variables[0].Name);
        Assert.Equal(1, asset.CustomEvents.Count);
        Assert.Equal("OnHit", asset.CustomEvents[0].Name);
        Assert.Equal(1, asset.CustomEvents[0].Parameters.Count);
        Assert.Equal("Damage", asset.CustomEvents[0].Parameters[0].Name);
    }

    // SC5: Throws on WithIntent / WithHostings for non-AiPrimitive builders.
    [Fact]
    public void WithIntent_OnNonAiPrimitive_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BlueprintAssetBuilder.Library("L").WithIntent(AiPrimitiveIntent.Condition));

        Assert.Throws<InvalidOperationException>(() =>
            BlueprintAssetBuilder.Instance("I").WithHostings(AiPrimitiveHosting.BTreeCondition));
    }

    // SC6: Graph Entry -> Delay -> Return produces correct node/link topology.
    [Fact]
    public void WithGraph_EntryDelayReturn_CorrectTopology()
    {
        var asset = BlueprintAssetBuilder
            .Library("Seq")
            .WithGraph("G", g => g.Entry().Delay(2.0f).Return(NodeStatus.Success))
            .Build();

        var graph = asset.Graphs[0];

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(2, graph.Links.Count);

        var entryNode = graph.Nodes.OfType<EventEntryNode>().Single();
        var delayNode = graph.Nodes.OfType<LatentDelayNode>().Single();
        var returnNode = graph.Nodes.OfType<ReturnNode>().Single();

        // First link: Entry -> Delay
        var link0 = graph.Links[0];
        Assert.Equal(entryNode.Id, link0.FromNodeId);
        Assert.Equal(delayNode.Id, link0.ToNodeId);

        // Second link: Delay -> Return
        var link1 = graph.Links[1];
        Assert.Equal(delayNode.Id, link1.FromNodeId);
        Assert.Equal(returnNode.Id, link1.ToNodeId);
    }

    // Extra: AiPrimitive factory pre-initializes Intent to Action.
    [Fact]
    public void AiPrimitive_FactoryPreInitializesIntentAndEmptyHostings()
    {
        var asset = BlueprintAssetBuilder.AiPrimitive("TestPrim").Build();

        Assert.NotNull(asset.Primitive);
        Assert.Equal(AiPrimitiveIntent.Action, asset.Primitive!.Intent);
        Assert.NotNull(asset.Primitive.Hostings);
        Assert.Empty(asset.Primitive.Hostings);
    }

    // Extra: Header is always set correctly.
    [Fact]
    public void Build_HeaderIsSetCorrectly()
    {
        var asset = BlueprintAssetBuilder.Library("Any").Build();

        Assert.Equal("Hrot.Blueprints", asset.Header.SubsystemType);
        Assert.Equal("1.0", asset.Header.SchemaVersion);
    }
}

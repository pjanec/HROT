using System.Numerics;
using System.Reflection;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.Editor.AiShared.Tests.Layout;

// Helper class providing static layout factory methods for LayoutDiscovery tests.
internal static class LayoutTestFixtures
{
    public const string TestBTreeAssetId = "f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21";
    public const string TestHsmAssetId   = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    public const string OtherAssetId     = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [BTreeLayout(TestBTreeAssetId)]
    public static BTreeEditorLayout SampleBTreeLayout() =>
        new BTreeEditorLayoutBuilder()
            .Canvas(new Vector2(12f, -34f), 1.5f)
            .Node(Guid.NewGuid().ToString("D"), new Vector2(100f, 200f))
            .Build();

    [HsmLayout(TestHsmAssetId)]
    public static HsmEditorLayout SampleHsmLayout() =>
        new HsmEditorLayoutBuilder()
            .Canvas(new Vector2(0f, 0f), 1.0f)
            .Build();
}

public sealed class LayoutDiscoveryTests
{
    private static readonly Assembly TestAssembly = Assembly.GetExecutingAssembly();

    [Fact]
    public void TryGetLayout_ReturnsLayout_WhenMethodExists()
    {
        var id = Guid.Parse(LayoutTestFixtures.TestBTreeAssetId);
        var result = LayoutDiscovery.TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>(
            TestAssembly, id);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryGetLayout_ReturnsCorrectPanOffset()
    {
        var id = Guid.Parse(LayoutTestFixtures.TestBTreeAssetId);
        var result = LayoutDiscovery.TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>(
            TestAssembly, id);
        Assert.Equal(new Vector2(12f, -34f), result!.PanOffset);
    }

    [Fact]
    public void TryGetLayout_ReturnsHsmLayout_WhenMethodExists()
    {
        var id = Guid.Parse(LayoutTestFixtures.TestHsmAssetId);
        var result = LayoutDiscovery.TryGetLayout<HsmLayoutAttribute, HsmEditorLayout>(
            TestAssembly, id);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryGetLayout_ReturnsNull_WhenAssetIdDoesNotMatch()
    {
        var wrongId = Guid.Parse(LayoutTestFixtures.OtherAssetId);
        var result = LayoutDiscovery.TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>(
            TestAssembly, wrongId);
        Assert.Null(result);
    }

    [Fact]
    public void TryGetLayout_ReturnsNull_WhenWrongAttributeType()
    {
        // Try to find a BTreeLayout using HsmLayout attribute -- should not match.
        var id = Guid.Parse(LayoutTestFixtures.TestBTreeAssetId);
        var result = LayoutDiscovery.TryGetLayout<HsmLayoutAttribute, HsmEditorLayout>(
            TestAssembly, id);
        Assert.Null(result);
    }

    [Fact]
    public void BTreeEditorLayoutBuilder_Node_StoredByGuid()
    {
        var nodeId = Guid.NewGuid();
        var layout = new BTreeEditorLayoutBuilder()
            .Canvas(Vector2.Zero, 1.0f)
            .Node(nodeId.ToString("D"), new Vector2(10f, 20f))
            .Build();
        Assert.True(layout.Nodes.ContainsKey(nodeId));
    }

    [Fact]
    public void BTreeEditorLayoutBuilder_Canvas_Stored()
    {
        var layout = new BTreeEditorLayoutBuilder()
            .Canvas(new Vector2(5f, 7f), 2.0f)
            .Build();
        Assert.Equal(new Vector2(5f, 7f), layout.PanOffset);
        Assert.Equal(2.0f, layout.ZoomLevel);
    }

    [Fact]
    public void HsmEditorLayoutBuilder_State_StoredByStableId()
    {
        var stateId = Guid.NewGuid();
        var layout = new HsmEditorLayoutBuilder()
            .Canvas(Vector2.Zero, 1.0f)
            .State(stateId.ToString("D"), new Vector2(10f, 20f))
            .Build();
        Assert.True(layout.States.ContainsKey(stateId));
    }

    [Fact]
    public void HsmEditorLayoutBuilder_Transition_StoredByVisualId()
    {
        var transId = Guid.NewGuid();
        var layout = new HsmEditorLayoutBuilder()
            .Canvas(Vector2.Zero, 1.0f)
            .Transition(transId.ToString("D"),
                new[] { new Vector2(0f, 0f), new Vector2(10f, 10f) })
            .Build();
        Assert.True(layout.Transitions.ContainsKey(transId));
    }

    [Fact]
    public void HsmEditorLayoutBuilder_Region_Stored()
    {
        var regionId = Guid.NewGuid();
        var layout = new HsmEditorLayoutBuilder()
            .Canvas(Vector2.Zero, 1.0f)
            .Region(regionId.ToString("D"), 0, new Vector2(50f, 60f))
            .Build();
        Assert.True(layout.Regions.ContainsKey(regionId));
    }
}

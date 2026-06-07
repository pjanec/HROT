using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Renderers;
using Hrot.Blueprints.Tests.Debug;
using NodeEditor.Core.Canvas;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class BlueprintRuntimeOverlayRendererTests
{
    private static BlueprintAsset CreateTestAsset(Guid nodeId)
    {
        return new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "TestBP",
            Graphs  =
            {
                new Graph
                {
                    Id   = Guid.NewGuid(),
                    Name = "EventGraph",
                    Kind = GraphKind.Event,
                    Nodes =
                    {
                        new SequenceNode
                        {
                            Id             = nodeId,
                            EditorMetadata = new NodeMetadata { X = 100f, Y = 200f },
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void RuntimeOverlay_IsActive_False_WhenNullSession()
    {
        var nodeId   = Guid.NewGuid();
        var asset    = CreateTestAsset(nodeId);
        var renderer = new BlueprintRuntimeOverlayRenderer(asset);
        Assert.False(renderer.IsActive);
    }

    [Fact]
    public void RuntimeOverlay_IsActive_True_WhenSessionSet()
    {
        var nodeId   = Guid.NewGuid();
        var asset    = CreateTestAsset(nodeId);
        var session  = new CapturingDebugSession();
        var renderer = new BlueprintRuntimeOverlayRenderer(asset);
        renderer.SetSession(session);
        Assert.True(renderer.IsActive);
    }

    [Fact]
    public void RuntimeOverlay_Id_IsStable()
    {
        var nodeId = Guid.NewGuid();
        var asset  = CreateTestAsset(nodeId);
        var a = new BlueprintRuntimeOverlayRenderer(asset);
        var b = new BlueprintRuntimeOverlayRenderer(asset);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal("blueprint.runtime_overlay", a.Id);
    }

    [Fact]
    public void RuntimeOverlay_Pass_IsAfterNodes()
    {
        var nodeId   = Guid.NewGuid();
        var asset    = CreateTestAsset(nodeId);
        var renderer = new BlueprintRuntimeOverlayRenderer(asset);
        Assert.Equal(CanvasRenderPass.AfterNodes, renderer.Pass);
    }

    [Fact]
    public void RuntimeOverlay_SetSession_Null_MakesInactive()
    {
        var nodeId   = Guid.NewGuid();
        var asset    = CreateTestAsset(nodeId);
        var session  = new CapturingDebugSession();
        var renderer = new BlueprintRuntimeOverlayRenderer(asset);

        renderer.SetSession(session);
        Assert.True(renderer.IsActive);

        renderer.SetSession(null);
        Assert.False(renderer.IsActive);
    }
}

using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Renderers;
using Hrot.Blueprints.Tests.Debug;
using NodeEditor.Core.Canvas;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class BlueprintBreakpointGutterRendererTests
{
    private static BlueprintAsset CreateTestAsset()
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
                            Id             = Guid.NewGuid(),
                            EditorMetadata = new NodeMetadata { X = 100f, Y = 200f },
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void GutterRenderer_IsActive_False_WhenNullSession()
    {
        var asset    = CreateTestAsset();
        var renderer = new BlueprintBreakpointGutterRenderer(asset);
        Assert.False(renderer.IsActive);
    }

    [Fact]
    public void GutterRenderer_IsActive_True_WhenSessionSet()
    {
        var asset    = CreateTestAsset();
        var session  = new CapturingDebugSession();
        var renderer = new BlueprintBreakpointGutterRenderer(asset);
        renderer.SetSession(session);
        Assert.True(renderer.IsActive);
    }

    [Fact]
    public void GutterRenderer_Id_IsStable()
    {
        var asset = CreateTestAsset();
        var a = new BlueprintBreakpointGutterRenderer(asset);
        var b = new BlueprintBreakpointGutterRenderer(asset);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal("blueprint.breakpoint_gutter", a.Id);
    }

    [Fact]
    public void GutterRenderer_Pass_IsAfterNodes()
    {
        var asset    = CreateTestAsset();
        var renderer = new BlueprintBreakpointGutterRenderer(asset);
        Assert.Equal(CanvasRenderPass.AfterNodes, renderer.Pass);
    }

    [Fact]
    public void GutterRenderer_SetSession_Null_MakesInactive()
    {
        var asset    = CreateTestAsset();
        var session  = new CapturingDebugSession();
        var renderer = new BlueprintBreakpointGutterRenderer(asset);

        renderer.SetSession(session);
        Assert.True(renderer.IsActive);

        renderer.SetSession(null);
        Assert.False(renderer.IsActive);
    }
}

using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class WhenFiringPulseRendererTests
{
    [Fact]
    public void Renderer_IsActive_InDebugMode()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.True(renderer.IsActive);
    }

    [Fact]
    public void Renderer_IsNotActive_InReleaseMode()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: false);
        Assert.False(renderer.IsActive);
    }

    [Fact]
    public void Renderer_Id_IsStable()
    {
        var a = new WhenFiringPulseRenderer(isDebugMode: true);
        var b = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public void OnNodeFired_DebugMode_AddsPendingPulse()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        var nodeId   = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(nodeId);
        Assert.True(renderer.HasPulse(nodeId));
        Assert.Equal(1, renderer.ActivePulseCount);
    }

    [Fact]
    public void OnNodeFired_ReleaseMode_DoesNotAddPulse()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: false);
        var nodeId   = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(nodeId);
        Assert.False(renderer.HasPulse(nodeId));
        Assert.Equal(0, renderer.ActivePulseCount);
    }

    [Fact]
    public void OnNodeFired_MultipleFires_AllTracked()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        var id1 = new NodeId(Guid.NewGuid());
        var id2 = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(id1);
        renderer.OnNodeFired(id2);
        Assert.Equal(2, renderer.ActivePulseCount);
    }

    [Fact]
    public void OnNodeFired_SameNode_ResetsTimer()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        var nodeId   = new NodeId(Guid.NewGuid());
        renderer.OnNodeFired(nodeId);
        renderer.OnNodeFired(nodeId);   // re-fires same node
        // Should still be 1 pulse (not 2), reset to full duration
        Assert.Equal(1, renderer.ActivePulseCount);
    }

    [Fact]
    public void Renderer_Pass_IsAfterNodes()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.Equal(CanvasRenderPass.AfterNodes, renderer.Pass);
    }

    [Fact]
    public void Renderer_NoFires_ZeroAllocations_ActivePulseCountIsZero()
    {
        var renderer = new WhenFiringPulseRenderer(isDebugMode: true);
        Assert.Equal(0, renderer.ActivePulseCount);
    }
}

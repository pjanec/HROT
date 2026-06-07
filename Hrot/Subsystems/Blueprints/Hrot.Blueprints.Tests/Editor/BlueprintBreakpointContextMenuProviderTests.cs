using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Tests.Debug;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class BlueprintBreakpointContextMenuProviderTests
{
    private static (
        BlueprintBreakpointContextMenuProvider Provider,
        CapturingDebugSession Session,
        Guid AssetId,
        Guid GraphId,
        Guid NodeId) CreateProvider()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var session = new CapturingDebugSession();
        var provider = new BlueprintBreakpointContextMenuProvider(session, assetId, graphId);
        return (provider, session, assetId, graphId, nodeId);
    }

    [Fact]
    public void ContextMenuProvider_RendererId_MatchesGutterRenderer()
    {
        var (provider, _, _, _, _) = CreateProvider();
        Assert.Equal("blueprint.breakpoint_gutter", provider.RendererId);
    }

    [Fact]
    public void ContextMenuProvider_ToggleBreakpoint_AddsBreakpointToSession()
    {
        var (provider, session, assetId, graphId, nodeId) = CreateProvider();
        var elementKey = nodeId.ToString("D");

        var items = provider.GetItemsFor(elementKey, default);
        Assert.NotEmpty(items);

        // Should have "Toggle Breakpoint" since no breakpoint exists yet.
        var toggleItem = items.FirstOrDefault(i => i.Label == "Toggle Breakpoint");
        Assert.NotNull(toggleItem);

        // Execute the toggle callback.
        toggleItem!.Execute();

        // Verify the session now has the breakpoint.
        var bps = session.GetBreakpoints();
        Assert.NotEmpty(bps);
        var bp = bps.First();
        Assert.Equal(assetId, bp.AssetId);
        Assert.Equal(graphId, bp.GraphId);
        Assert.Equal(nodeId.ToString("D"), bp.NodeId);
        Assert.True(bp.Enabled);
    }

    [Fact]
    public void ContextMenuProvider_ClearBreakpoint_RemovesBreakpointFromSession()
    {
        var (provider, session, assetId, graphId, nodeId) = CreateProvider();

        // Pre-register a breakpoint on the session.
        session.SetBreakpoint(assetId, graphId, nodeId);
        Assert.NotEmpty(session.GetBreakpoints());

        var elementKey = nodeId.ToString("D");
        var items = provider.GetItemsFor(elementKey, default);
        Assert.NotEmpty(items);

        // Should have "Clear Breakpoint" since a breakpoint exists.
        var clearItem = items.FirstOrDefault(i => i.Label == "Clear Breakpoint");
        Assert.NotNull(clearItem);

        // Execute the clear callback.
        clearItem!.Execute();

        // Verify the session no longer has the breakpoint.
        var bps = session.GetBreakpoints();
        Assert.DoesNotContain(bps, bp => bp.NodeId == nodeId.ToString("D"));
    }

    [Fact]
    public void ContextMenuProvider_NoItems_ForInvalidElementKey()
    {
        var (provider, _, _, _, _) = CreateProvider();
        var items = provider.GetItemsFor("not-a-guid", default);
        Assert.Empty(items);
    }

    [Fact]
    public void ContextMenuProvider_ToggleThenClear_ProducesCorrectMenuSequence()
    {
        var (provider, session, assetId, graphId, nodeId) = CreateProvider();
        var elementKey = nodeId.ToString("D");

        // First call: should show Toggle Breakpoint.
        var items1 = provider.GetItemsFor(elementKey, default);
        Assert.Contains(items1, i => i.Label == "Toggle Breakpoint");

        // Execute toggle.
        items1.First(i => i.Label == "Toggle Breakpoint").Execute();
        Assert.NotEmpty(session.GetBreakpoints());

        // Second call: should show Clear Breakpoint.
        var items2 = provider.GetItemsFor(elementKey, default);
        Assert.Contains(items2, i => i.Label == "Clear Breakpoint");

        // Execute clear.
        items2.First(i => i.Label == "Clear Breakpoint").Execute();
        Assert.DoesNotContain(session.GetBreakpoints(), bp => bp.NodeId == nodeId.ToString("D"));

        // Third call: should show Toggle Breakpoint again.
        var items3 = provider.GetItemsFor(elementKey, default);
        Assert.Contains(items3, i => i.Label == "Toggle Breakpoint");
    }
}

using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

/// <summary>
/// CE-1010 — the node menu's "Toggle Breakpoint" was always enabled and a silent no-op in every host that registered no
/// handler (BTree, HSM). It is now greyed there, and still follows the host's predicate where one is registered.
/// </summary>
public sealed class ToggleBreakpointItemTests
{
    private static EditorCommandsImpl WithToggle(bool enabled)
    {
        var commands = new EditorCommandsImpl();
        commands.Register(new EditorCommandDescriptor(CommandCatalog.ToggleBreakpoint, "Toggle Breakpoint", "Debug", null, null, null, () => enabled), _ => { });
        return commands;
    }

    [Fact]
    public void CE1010_NoHandler_TheItemIsGreyed()
    {
        Assert.False(CanvasRenderer.ToggleBreakpointEnabled(null, targetSelected: true));
        Assert.False(CanvasRenderer.ToggleBreakpointEnabled(new EditorCommandsImpl(), targetSelected: true));
        Assert.False(CanvasRenderer.ToggleBreakpointEnabled(new EditorCommandsImpl(), targetSelected: false));
    }

    [Fact]
    public void CE1010_ASelectedNode_FollowsTheHostsPredicate()
    {
        Assert.True(CanvasRenderer.ToggleBreakpointEnabled(WithToggle(true), targetSelected: true));
        Assert.False(CanvasRenderer.ToggleBreakpointEnabled(WithToggle(false), targetSelected: true));
    }

    [Fact]
    public void CE1010_AnUnselectedNode_IsOffered_WhenAHandlerExists()
    {
        // the selection moves to the node only on click, so the predicate (which reads the selection) cannot judge it yet
        Assert.True(CanvasRenderer.ToggleBreakpointEnabled(WithToggle(false), targetSelected: false));
    }
}

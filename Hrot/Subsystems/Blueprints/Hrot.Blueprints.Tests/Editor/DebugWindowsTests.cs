using Hrot.Blueprints.Editor.Debug;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class DebugWindowsTests
{
    [Fact]
    public void DebugPanelWindow_Title_Reflects_PauseState()
    {
        var session = new MockDebugSession { IsPaused = false };
        var window  = new DebugPanelWindow(session);

        Assert.DoesNotContain("PAUSED", window.Title);

        session.IsPaused = true;
        Assert.Contains("PAUSED", window.Title);
    }

    [Fact]
    public void WatchPanelWindow_OnActivated_Subscribes_OnDeactivated_Unsubscribes()
    {
        var session = new MockDebugSession();
        var window  = new WatchPanelWindow(session);

        window.OnActivated();
        Assert.Equal(1, session.PinValueChangedSubscriberCount);

        window.OnDeactivated();
        Assert.Equal(0, session.PinValueChangedSubscriberCount);
    }
}

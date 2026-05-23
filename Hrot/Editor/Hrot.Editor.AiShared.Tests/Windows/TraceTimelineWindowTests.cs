using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

public class TraceTimelineWindowTests
{
    private static TraceTimelineWindow CreateWindow() =>
        new TraceTimelineWindow(new EditorSelectionStore(), new DebugSessionRegistry());

    private sealed class StubProvider : ITraceLaneProvider
    {
        public AssetKind Kind => AssetKind.BTree;
        public IReadOnlyList<TraceLaneDescriptor> Lanes => Array.Empty<TraceLaneDescriptor>();
    }

    [Fact]
    public void Constructor_SetsId()
    {
        var window = CreateWindow();
        Assert.Equal("ai_trace_timeline", window.Id);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        var window = CreateWindow();
        Assert.Equal("Trace Timeline", window.Title);
    }

    [Fact]
    public void Constructor_SetsScopePerspectiveBound()
    {
        var window = CreateWindow();
        Assert.Equal(WindowScope.PerspectiveBound, window.Scope);
    }

    [Fact]
    public void RegisterProvider_AddsProvider()
    {
        var window = CreateWindow();
        Assert.Equal(0, window.RegisteredProviderCount);
        window.RegisterProvider(new StubProvider());
        Assert.Equal(1, window.RegisteredProviderCount);
    }

    [Fact]
    public void RegisterProvider_MultipleCanBeRegistered()
    {
        var window = CreateWindow();
        window.RegisterProvider(new StubProvider());
        window.RegisterProvider(new StubProvider());
        window.RegisterProvider(new StubProvider());
        Assert.Equal(3, window.RegisteredProviderCount);
    }
}

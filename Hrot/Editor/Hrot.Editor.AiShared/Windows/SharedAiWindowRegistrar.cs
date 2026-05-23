using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Registers the four shared AI editor windows with the WindowManager.
/// Implement IWindowRegistrar so the subsystem orchestrator can call RegisterWindows.
/// </summary>
public sealed class SharedAiWindowRegistrar : IWindowRegistrar
{
    private readonly AssetBrowserWindow _assetBrowser;
    private readonly InspectorWindow _inspector;
    private readonly RuntimeInspectorWindow _runtimeInspector;
    private readonly TraceTimelineWindow _traceTimeline;

    public SharedAiWindowRegistrar(
        AssetBrowserWindow assetBrowser,
        InspectorWindow inspector,
        RuntimeInspectorWindow runtimeInspector,
        TraceTimelineWindow traceTimeline)
    {
        _assetBrowser = assetBrowser;
        _inspector = inspector;
        _runtimeInspector = runtimeInspector;
        _traceTimeline = traceTimeline;
    }

    public void RegisterWindows(WindowManager windowManager)
    {
        windowManager.RegisterWindow(_assetBrowser);
        windowManager.RegisterWindow(_inspector);
        windowManager.RegisterWindow(_runtimeInspector);
        windowManager.RegisterWindow(_traceTimeline);
    }
}

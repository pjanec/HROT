using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Registers the shared AI editor windows with the WindowManager.
/// Implement IWindowRegistrar so the subsystem orchestrator can call RegisterWindows.
/// </summary>
public sealed class SharedAiWindowRegistrar : IWindowRegistrar
{
    private readonly AssetBrowserDockedWindow _assetBrowser;
    private readonly InspectorWindow _inspector;
    private readonly RuntimeInspectorWindow _runtimeInspector;
    private readonly TraceTimelineWindow _traceTimeline;
    private readonly FindResultsWindow _findResults;
    private readonly BlackboardAuthoringWindow _blackboardAuthoring;
    private readonly ComparisonSummaryPanel _comparisonSummary;
    private readonly ComparisonSidebar _comparisonSidebar;

    public SharedAiWindowRegistrar(
        AssetBrowserDockedWindow assetBrowser,
        InspectorWindow inspector,
        RuntimeInspectorWindow runtimeInspector,
        TraceTimelineWindow traceTimeline,
        FindResultsWindow findResults,
        BlackboardAuthoringWindow blackboardAuthoring,
        ComparisonSummaryPanel comparisonSummary,
        ComparisonSidebar comparisonSidebar)
    {
        _assetBrowser        = assetBrowser;
        _inspector           = inspector;
        _runtimeInspector    = runtimeInspector;
        _traceTimeline       = traceTimeline;
        _findResults         = findResults;
        _blackboardAuthoring = blackboardAuthoring;
        _comparisonSummary   = comparisonSummary;
        _comparisonSidebar   = comparisonSidebar;
    }

    public void RegisterWindows(WindowManager windowManager)
    {
        windowManager.RegisterWindow(_assetBrowser);
        windowManager.RegisterWindow(_inspector);
        windowManager.RegisterWindow(_runtimeInspector);
        windowManager.RegisterWindow(_traceTimeline);
        windowManager.RegisterWindow(_findResults);
        windowManager.RegisterWindow(_blackboardAuthoring);
        windowManager.RegisterWindow(_comparisonSummary);
        windowManager.RegisterWindow(_comparisonSidebar);
    }
}

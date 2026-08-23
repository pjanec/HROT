using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Registers the shared AI editor windows with the WindowManager.
///
/// <para>⛔⛔ <b>S5 (2026-08-22): the InspectorWindow parameter is GONE — the window is retired</b>
/// (<c>BP-399</c> §7.6 ⑤; all six of its arms are Details views or menu items now).</para>
///
/// <para>⚠⚠ <b>MEASURED WHILE DOING SO, and NOT acted on: this class has ZERO constructions in the
/// repository.</b> The live registration path is <c>PerspectiveWorkspaceRegistrar</c>. ⛔ Filed as a
/// finding rather than deleted — 📌 the <c>2026-08-15</c> rule: <i>"what is not used does not mean it is
/// existing without reason"</i>, and a registrar is exactly the shape a host outside this repo might
/// call. ⭐ It needs a design-corpus sweep of its own before removal.</para>
/// Implement IWindowRegistrar so the subsystem orchestrator can call RegisterWindows.
/// </summary>
public sealed class SharedAiWindowRegistrar : IWindowRegistrar
{
    private readonly AssetBrowserDockedWindow _assetBrowser;
    private readonly RuntimeInspectorWindow _runtimeInspector;
    private readonly TraceTimelineWindow _traceTimeline;
    private readonly FindResultsWindow _findResults;
    private readonly BlackboardAuthoringWindow _blackboardAuthoring;
    private readonly ComparisonSummaryPanel _comparisonSummary;
    private readonly ComparisonSidebar _comparisonSidebar;

    public SharedAiWindowRegistrar(
        AssetBrowserDockedWindow assetBrowser,
        RuntimeInspectorWindow runtimeInspector,
        TraceTimelineWindow traceTimeline,
        FindResultsWindow findResults,
        BlackboardAuthoringWindow blackboardAuthoring,
        ComparisonSummaryPanel comparisonSummary,
        ComparisonSidebar comparisonSidebar)
    {
        _assetBrowser        = assetBrowser;
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
        windowManager.RegisterWindow(_runtimeInspector);
        windowManager.RegisterWindow(_traceTimeline);
        windowManager.RegisterWindow(_findResults);
        windowManager.RegisterWindow(_blackboardAuthoring);
        windowManager.RegisterWindow(_comparisonSummary);
        windowManager.RegisterWindow(_comparisonSidebar);
    }
}

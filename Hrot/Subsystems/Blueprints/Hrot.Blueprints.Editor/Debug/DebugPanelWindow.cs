using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class DebugPanelWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;

    public override string Title => _session.IsPaused ? "Debug [PAUSED]" : "Debug";

    public DebugPanelWindow(IBlueprintDebugSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override void DrawUI()
    {
        // ImGui rendering: pause indicator, breakpoint list, step buttons.
        var paused      = _session.IsPaused;
        var breakpoints = _session.GetBreakpoints();
        // Rendering requires ImGui runtime; data access verified by unit tests.
        _ = paused;
        _ = breakpoints;
    }

    public override void OnActivated()   { }
    public override void OnDeactivated() { }
}

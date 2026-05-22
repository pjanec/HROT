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
        // Requires ImGui runtime. Stub for Slice 1.
    }

    public override void OnActivated()   { }
    public override void OnDeactivated() { }
}

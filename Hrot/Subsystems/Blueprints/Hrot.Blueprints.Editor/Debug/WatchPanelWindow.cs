using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class WatchPanelWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;

    public override string Title => "Watches";

    public WatchPanelWindow(IBlueprintDebugSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override void OnActivated()
        => _session.OnPinValueChangedEvent += HandlePinValueChanged;

    public override void OnDeactivated()
        => _session.OnPinValueChangedEvent -= HandlePinValueChanged;

    private void HandlePinValueChanged(PinValueChanged evt) { /* refresh row data */ }

    public override void DrawUI()
    {
        // ImGui table: Name, Type, Value, Tick, Stale? -- requires ImGui runtime.
        var watches = _session.GetWatches();
        _ = watches;
    }
}

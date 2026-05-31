using ImGuiNET;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class WatchPanelWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;

    public override string Title => "Watches";

    // Captured on each DrawUI call -- readable by tests without an ImGui context.
    public IReadOnlyList<Watch>? LastRenderedWatches { get; private set; }

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
        var watches = _session.GetWatches();

        LastRenderedWatches = watches;

        // ImGui rendering requires a live context; skip in headless / test environments.
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        if (ImGui.BeginTable("##watchTable", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Type");
            ImGui.TableSetupColumn("Value");
            ImGui.TableSetupColumn("Tick");
            ImGui.TableHeadersRow();

            foreach (var w in watches)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(w.IsStale ? $"{w.DisplayName} [stale]" : w.DisplayName);
                ImGui.TableNextColumn();
                ImGui.Text(w.ExpectedType.Name);
                ImGui.TableNextColumn();
                ImGui.Text(w.HasEverBeenWritten
                    ? Convert.ToHexString(w.LastValueBytes)
                    : "--");
                ImGui.TableNextColumn();
                ImGui.Text(w.HasEverBeenWritten ? w.LastUpdateTick.ToString() : "--");
            }

            ImGui.EndTable();
        }
    }
}

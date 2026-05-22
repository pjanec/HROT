namespace Hrot.Blueprints.Editor.Debug;

public sealed class HotReloadLogWindow : BlueprintEditorWindowBase
{
    public HotReloadLogModel Model { get; } = new();

    public override string Title => "Hot Reload Log";

    public void OnReloadCompleted(ReloadCompletedInfo info)
    {
        Model.AddEntry(new ReloadLogEntry(
            Timestamp:  DateTime.UtcNow,
            Source:     info.Source,
            Succeeded:  true,
            Message:    $"{info.ReloadedAssetIds.Length} asset(s) reloaded in {info.DurationMs}ms",
            DurationMs: info.DurationMs));
    }

    public void OnReloadFailed(string message, ReloadSource source)
    {
        Model.AddEntry(new ReloadLogEntry(
            Timestamp:  DateTime.UtcNow,
            Source:     source,
            Succeeded:  false,
            Message:    message,
            DurationMs: 0));
    }

    public override void DrawUI()
    {
        // ImGui scrollable table with Clear button -- requires ImGui runtime.
    }
}

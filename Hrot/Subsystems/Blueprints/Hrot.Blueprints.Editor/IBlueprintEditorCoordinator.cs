namespace Hrot.Blueprints.Editor;

/// <summary>
/// Supplies hot-reload lifecycle events to editor windows that need to react to reloads.
/// </summary>
public interface IBlueprintEditorCoordinator
{
    event Action<ReloadCompletedInfo>? OnReloadCompleted;
    event Action<string, ReloadSource>? OnReloadFailed;
}

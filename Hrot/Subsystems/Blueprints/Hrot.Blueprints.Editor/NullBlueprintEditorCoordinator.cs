namespace Hrot.Blueprints.Editor;

/// <summary>
/// No-op <see cref="IBlueprintEditorCoordinator"/> for contexts where hot-reload
/// lifecycle events do not need to propagate to the Hot Reload Log window.
/// </summary>
public sealed class NullBlueprintEditorCoordinator : IBlueprintEditorCoordinator
{
    // Custom accessors suppress CS0067 (unused event) in -warnaserror builds.
    public event Action<ReloadCompletedInfo>? OnReloadCompleted { add { } remove { } }
    public event Action<string, ReloadSource>? OnReloadFailed   { add { } remove { } }
}

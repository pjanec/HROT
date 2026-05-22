namespace Hrot.Blueprints.Editor;

public enum ReloadSource
{
    QuickReloadViaApi,
    FullRebuildViaFileWatcher,
}

public sealed record ReloadCompletedInfo(
    ReloadSource Source,
    Guid[] ReloadedAssetIds,
    string? DllPath,
    long DurationMs);

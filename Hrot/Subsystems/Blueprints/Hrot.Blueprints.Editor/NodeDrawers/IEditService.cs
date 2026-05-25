namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Blueprint node edit command dispatcher. Provides undo/redo integration.
/// Stub for M5; full implementation deferred.
/// </summary>
public interface IEditService
{
    void MarkDirty(Hrot.Blueprints.Core.Assets.BlueprintAsset asset);
}

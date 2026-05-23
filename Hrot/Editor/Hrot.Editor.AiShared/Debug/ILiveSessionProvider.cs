namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Provides live entity counts for assets that have active debug sessions attached.
/// The count reflects how many entities are currently running under the given asset.
/// </summary>
public interface ILiveSessionProvider
{
    /// <summary>
    /// Returns the number of entities actively running the specified asset.
    /// Returns 0 if no session is registered for this asset or no entity is attached.
    /// </summary>
    int GetActiveEntityCount(Guid assetId);
}

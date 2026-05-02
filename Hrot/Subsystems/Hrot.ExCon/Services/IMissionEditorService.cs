using Hrot.Core.Mission;
using Hrot.Core.Network;

namespace Hrot.ExCon.Services;

/// <summary>
/// Provides mission snapshot reads and asynchronous commit of mission plan
/// changes with optimistic concurrency support.
/// </summary>
public interface IMissionEditorService : IDisposable
{
    /// <summary>
    /// Returns the available behaviour names for the given entity, filtered to
    /// behaviors that are valid for the entity's TKB type.  Returns an empty
    /// list when the entity is not found.
    /// </summary>
    /// <param name="entityId">The network entity ID.</param>
    IReadOnlyList<string> GetAvailableBehaviors(long entityId);

    /// <summary>
    /// Returns the current <see cref="MissionPlan"/> and its optimistic-lock
    /// version for the given entity, or <c>(null, 0)</c> if not found.
    /// </summary>
    (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId);

    /// <summary>
    /// Sends a full-mission-replace command and waits asynchronously for the
    /// CGF to acknowledge it.
    /// </summary>
    /// <param name="entityId">Target entity.</param>
    /// <param name="newPlan">The replacement mission plan.</param>
    /// <param name="baseVersion">
    /// The version the caller believes is current. The server will reject the
    /// request (ERR_VERSION_CONFLICT) if the actual version is greater.
    /// </param>
    Task<MissionCommitResult> CommitMissionAsync(
        long entityId, MissionPlan newPlan, long baseVersion);

    /// <summary>
    /// Sends an imperative control command (Jump, Abort, etc.) and waits
    /// asynchronously for the CGF to acknowledge it, returning the new OCC version.
    /// Mirrors <see cref="CommitMissionAsync"/> but uses <c>BaseVersion = 0</c>
    /// because control commands bypass the optimistic-lock check.
    /// </summary>
    Task<MissionCommitResult> SendControlCommandAsync(
        long entityId, eMissionCommandType type, Guid taskId);

    /// <summary>
    /// Sends an imperative control command (Jump, Abort, etc.) without waiting
    /// for an acknowledgment.
    /// </summary>
    void SendControlCommand(
        long entityId, eMissionCommandType type, Guid taskId);
}

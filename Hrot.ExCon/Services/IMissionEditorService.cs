using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.ExCon.Services;

/// <summary>
/// The result returned when a mission commit request resolves (either via
/// ACK or timeout).
/// </summary>
public sealed class MissionCommitResult
{
    /// <summary>True if the server accepted and applied the mission change.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable error description. Null or empty on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The new optimistic-lock version after a successful commit. 0 on failure.
    /// </summary>
    public long NewVersion { get; init; }

    /// <summary>
    /// The numeric error code from the ACK response.  0 on success;
    /// 7 (<c>ERR_VERSION_CONFLICT</c>) when the server rejected the commit due to
    /// an optimistic-lock version mismatch.
    /// </summary>
    public int ErrorCode { get; init; }
}

/// <summary>
/// Provides mission snapshot reads and asynchronous commit of mission plan
/// changes with optimistic concurrency support.
/// </summary>
public interface IMissionEditorService : IDisposable
{
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

    /// <summary>
    /// Called by the network ingress layer whenever a
    /// <see cref="MissionControlAck"/> is received from the DDS bus.
    /// Resolves any matching pending commit.
    /// </summary>
    void OnAckReceived(MissionControlAck ack);
}

using Hrot.Core.Mission;
using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for reading and committing mission plans on a per-entity basis.
/// Provides an async/await API over optimistic-concurrency mission editing,
/// independent of the underlying transport or ECS implementation.
/// </summary>
public interface IMissionEditorService
{
    /// <summary>
    /// Returns the available behaviour names for the given entity, filtered to doctrines
    /// that are both registered in the live engine and valid for the entity's TKB type.
    /// </summary>
    /// <param name="entityId">The network entity ID.</param>
    IReadOnlyList<string> GetAvailableBehaviors(long entityId);

    /// <summary>
    /// Returns the current <see cref="MissionPlan"/> and its optimistic-lock version,
    /// or <c>(null, 0)</c> when the entity has no active mission.
    /// </summary>
    /// <param name="entityId">The network entity ID.</param>
    (Hrot.Core.Mission.MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId);

    /// <summary>
    /// Sends a full mission-replace command and waits asynchronously for acknowledgement.
    /// </summary>
    /// <param name="entityId">Target entity.</param>
    /// <param name="plan">The replacement mission plan.</param>
    /// <param name="baseVersion">
    /// The optimistic-lock version the caller believes is current.
    /// The server rejects the commit when the actual version is greater.
    /// </param>
    Task<MissionCommitResult> CommitMissionAsync(long entityId, Hrot.Core.Mission.MissionPlan plan, long baseVersion);

    /// <summary>
    /// Sends an imperative control command (e.g. jump-to-task, abort-all) and waits
    /// asynchronously for acknowledgement.
    /// </summary>
    /// <param name="entityId">Target entity.</param>
    /// <param name="type">The command discriminator.</param>
    /// <param name="taskId">
    /// The target task GUID for commands that address a specific task;
    /// use <see cref="Guid.Empty"/> for commands such as <c>CMD_ABORT_ALL</c>.
    /// </param>
    Task<MissionCommitResult> SendControlCommandAsync(long entityId, Hrot.Core.Mission.eMissionCommandType type, Guid taskId);
}

using System;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Payload for <see cref="NodeOpType.CommitState"/> intents.
    /// Replaces the previously boxed <c>int</c> state ID.
    /// </summary>
    public readonly record struct CommitStatePayload(int TargetStateId);

    /// <summary>
    /// Payload for <see cref="NodeOpType.NodeReplaySeek"/> intents.
    /// Replaces the previously boxed <c>long</c> wall-clock tick target.
    /// </summary>
    public readonly record struct ReplaySeekPayload(long TargetWallTicks);

    /// <summary>
    /// Result payload returned by a completed <see cref="NodeOpType.NodeReplaySeek"/> operation.
    /// Carries the <see cref="GlobalTime"/> the recording landed on after the seek.
    /// </summary>
    public readonly record struct ReplaySeekResult(GlobalTime RestoredTime);

    /// <summary>
    /// Result payload returned by a completed <see cref="NodeOpType.PrepareLive"/> Live-from-Replay
    /// branch operation.  Carries the historical <see cref="GlobalTime"/> captured immediately
    /// before the node tore down its replay module (CGF1-S0305 / RT-019).
    /// </summary>
    public readonly record struct LiveBranchResult(GlobalTime HistoricalTime);

    /// <summary>
    /// Payload for <see cref="NodeOpType.AbortTransaction"/> intents.
    /// Replaces the previously boxed <c>Guid</c> target transaction ID.
    /// </summary>
    public readonly record struct AbortTransactionPayload(Guid TargetTransactionId);
}

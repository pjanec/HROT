using System;

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
    /// Payload for <see cref="NodeOpType.AbortTransaction"/> intents.
    /// Replaces the previously boxed <c>Guid</c> target transaction ID.
    /// </summary>
    public readonly record struct AbortTransactionPayload(Guid TargetTransactionId);
}

using System;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Payload for <see cref="NodeOpType.CommitState"/> intents.
    /// Carries the target <see cref="ClusterState"/> enum value rather than a raw integer.
    /// </summary>
    public readonly record struct CommitStatePayload(ClusterState TargetState);

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

    /// <summary>
    /// Payload for the <see cref="NodeOpType.PrepareZone"/> / <see cref="NodeOpType.CommitZone"/>
    /// pair — ONE zone per round (📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.3).
    ///
    /// <para>⭐ It carries a zone id and nothing else <b>on purpose</b>: the zone's shape lives on the
    /// zone ENTITY, which every node already has (§5.1 — "there is no zone artefact"). Putting bounds on
    /// the wire would create a second place the truth can live, and the two could then disagree.
    /// ⚠ This is why it is NOT <c>mgmt-1</c> §11.2's <c>ZoneSpec</c>: that predates the
    /// entity-is-the-definition model and carries <c>Bounds</c> + <c>DataPath</c>.</para>
    /// </summary>
    public readonly record struct ZoneOpPayload(string ZoneId);

    /// <summary>
    /// Payload for the <see cref="NodeOpType.PrepareTerrainAsset"/> /
    /// <see cref="NodeOpType.CommitTerrainAsset"/> pair.
    ///
    /// <para><paramref name="Kinds"/> names the asset kinds to (re)build — the operator's selection in
    /// the cluster panel. ⚠ Empty means "everything", not "nothing": an op that asked for nothing would
    /// never be published. 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3.1.</para>
    /// </summary>
    public readonly record struct TerrainAssetOpPayload(string[]? Kinds);
}

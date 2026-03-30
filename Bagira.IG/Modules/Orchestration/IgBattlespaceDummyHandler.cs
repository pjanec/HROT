using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Orchestration;

namespace Bagira.IG.Modules.Orchestration
{
    /// <summary>
    /// Dummy DSM handler for battlespace-load operations on the IG node
    /// (CGF-1-BATCH-23 A.2).
    ///
    /// <para>
    /// IG is a pure network renderer and has no battlespace / terrain-DB state of its
    /// own.  The orchestrator may fan out <c>PrepareBattlespace</c> and
    /// <c>CommitBattlespace</c> commands to all roster members including IG when the
    /// cluster transitions through <c>LoadingEdit</c>.  This handler ensures those
    /// commands are acknowledged promptly so the 2PC round is not stalled.
    /// </para>
    ///
    /// <para>
    /// <b>Future work:</b> Full terrain-DB preload from scenario entities (receiving
    /// map-tile / height-map references embedded in the battlespace command payload)
    /// is not yet implemented.  When IG acquires a terrain-streaming subsystem, this
    /// handler should be replaced with a real implementation that kicks off the tile
    /// prefetch in <see cref="PrepareAsync"/> and signals completion in
    /// <see cref="Commit"/>.
    /// </para>
    /// </summary>
    public sealed class IgBattlespaceDummyHandler : IDsmHandler
    {
        /// <summary>Integer value of <see cref="NodeOpType.PrepareBattlespace"/> (stable).</summary>
        public const int PrepareBattlespaceOperationId = (int)NodeOpType.PrepareBattlespace;
        /// <summary>Integer value of <see cref="NodeOpType.CommitBattlespace"/> (stable).</summary>
        public const int CommitBattlespaceOperationId = (int)NodeOpType.CommitBattlespace;

        /// <inheritdoc />
        public bool CanHandle(int operationId)
            => operationId == PrepareBattlespaceOperationId
            || operationId == CommitBattlespaceOperationId;

        /// <inheritdoc />
        /// <remarks>
        /// No-op.  Returns <see langword="null"/> immediately (success) so that
        /// the DrillSlave can ACK the orchestrator without delay.
        /// Terrain-DB preload from scenario entities is future work.
        /// </remarks>
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            FdpLog<IgBattlespaceDummyHandler>.Info(
                "[IG] Battlespace op {0} — dummy ACK (terrain preload is future work).",
                (NodeOpType)cmd.OperationId);
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo) { }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo) { }
    }
}

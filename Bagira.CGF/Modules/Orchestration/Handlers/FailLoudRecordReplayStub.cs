using System;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace Bagira.CGF.Modules.Orchestration.Handlers
{
    /// <summary>
    /// Explicit fail-loud stub for recording and replay DSM operations on the CGF node.
    ///
    /// <para>
    /// Until CGF hosts a recordable <c>ModuleHostKernel</c> (Phase 3+ brain kernel),
    /// operations <see cref="NodeOpType.PrepareLive"/>, <see cref="NodeOpType.FinalizeLive"/>,
    /// <see cref="NodeOpType.PrepareReplay"/>, and <see cref="NodeOpType.FinalizeReplay"/>
    /// are <b>explicitly unsupported</b> on this node.
    /// </para>
    ///
    /// <para>
    /// This stub replaces the previous silent no-op (where the CGF DrillSlave simply logged
    /// "No handler for NodeOpCommand" at <c>Debug</c> level and returned success), closing
    /// the brain-side persistence gap identified in the BATCH-17 architecture note.
    /// </para>
    ///
    /// <para>
    /// <b>ACK / NAK behaviour:</b> Because the CGF <see cref="DrillSlave"/> does not yet
    /// expose a <c>NodeOpStatus</c> DDS writer, this handler cannot send a network-level NAK.
    /// Instead it logs an <c>Error</c> so the problem surfaces in structured logs.  Once CGF
    /// acquires a kernel and the same handler stack as SimHost, this stub must be removed and
    /// replaced with a real implementation (or the common
    /// <see cref="Bagira.SimHost.Modules.Orchestration.Handlers.LiveLoadDsmHandler"/> /
    /// <see cref="Bagira.SimHost.Modules.Orchestration.Handlers.ReplayLoadDsmHandler"/> path).
    /// </para>
    /// </summary>
    public sealed class FailLoudRecordReplayStub : IDsmHandler
    {
        private readonly string _nodeName;

        /// <param name="nodeName">
        /// Human-readable node name used in log messages (e.g. <c>"CGF"</c>).
        /// </param>
        public FailLoudRecordReplayStub(string nodeName = "CGF")
        {
            _nodeName = nodeName ?? throw new ArgumentNullException(nameof(nodeName));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns <c>true</c> for <see cref="NodeOpType.PrepareLive"/>,
        /// <see cref="NodeOpType.FinalizeLive"/>, <see cref="NodeOpType.PrepareReplay"/>, and
        /// <see cref="NodeOpType.FinalizeReplay"/> — all recording/replay lifecycle operations
        /// that are unsupported until CGF hosts a recordable kernel.
        /// </remarks>
        public bool CanHandle(NodeOpType op) =>
            op == NodeOpType.PrepareLive   ||
            op == NodeOpType.FinalizeLive  ||
            op == NodeOpType.PrepareReplay ||
            op == NodeOpType.FinalizeReplay;

        /// <summary>
        /// Logs an <c>Error</c> indicating that the operation is unsupported on this node
        /// until a recordable kernel is present.  Does not throw, so the DrillSlave dispatch
        /// loop continues processing subsequent commands.
        /// </summary>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            FdpLog<FailLoudRecordReplayStub>.Error(
                "[{0}] FailLoudRecordReplayStub: received unsupported recording/replay operation " +
                "{1} (transactionId={2}).  CGF does not yet host a recordable kernel — brain-side " +
                "persistence is incomplete.  Replace this stub when the CGF kernel is wired.",
                _nodeName, cmd.Operation, cmd.TransactionId);

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            // No state to commit; the error was already logged in PrepareAsync.
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            // No resources to release.
        }
    }
}

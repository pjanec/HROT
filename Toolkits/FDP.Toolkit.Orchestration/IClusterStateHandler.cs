using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;

namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Implemented by each per-subsystem component that participates in the
    /// Cluster State Machine two-phase-commit protocol.
    ///
    /// <para>
    /// The <c>ClusterSlave</c> calls handlers in sequence during the prepare /
    /// commit / abort phases of a distributed transaction.
    /// </para>
    ///
    /// <para>
    /// <b>Parameter naming:</b> <paramref name="operationId"/> carries the raw
    /// integer value of the operation type.  Hrot handlers may cast it back to
    /// <c>NodeOpType</c>; the integer values are identical and stable.  This
    /// interface uses <c>int</c> so FDP toolkit code stays free of Hrot-layer
    /// DDS enums.
    /// </para>
    /// </summary>
    public interface IClusterStateHandler
    {
        /// <summary>
        /// Returns <c>true</c> when this handler is responsible for the given
        /// <paramref name="operationId"/>.
        /// Callers may cast <paramref name="operationId"/> back to their specific
        /// enum type (e.g. <c>NodeOpType</c>) for internal switch statements.
        /// </summary>
        bool CanHandle(int operationId);

        /// <summary>
        /// Performs any async preparation work required before committing
        /// <paramref name="cmd"/>.  Return <c>null</c> on success or an error
        /// string on failure.  Must not mutate ECS state.
        /// </summary>
        Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct);

        /// <summary>
        /// Commits the previously prepared command.  Called from the main thread
        /// (inside <c>Tick()</c>).  May mutate ECS state via <paramref name="repo"/>.
        /// <paramref name="repo"/> is <c>null</c> for no-ECS subsystems (ExCon, CGF skeleton).
        /// </summary>
        void Commit(OrchestrationCommand cmd, EntityRepository? repo);

        /// <summary>
        /// Aborts the previously prepared command — rolls back any resources
        /// allocated during <see cref="PrepareAsync"/>.
        /// <paramref name="repo"/> is <c>null</c> for no-ECS subsystems (ExCon, CGF skeleton).
        /// </summary>
        void Abort(OrchestrationCommand cmd, EntityRepository? repo);
    }
}

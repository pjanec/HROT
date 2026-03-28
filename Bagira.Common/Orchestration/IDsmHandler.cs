using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Fdp.Kernel;

namespace Bagira.Common.Orchestration
{
    /// <summary>
    /// Implemented by each per-subsystem component that participates in the
    /// Drill State Machine two-phase-commit protocol.
    ///
    /// <para>
    /// Implementations live entirely in the Bagira application layer.
    /// No <c>FDP.*</c> project may reference or implement this interface.
    /// </para>
    ///
    /// <para>
    /// The <see cref="DrillSlave"/> calls handlers in sequence during
    /// the prepare / commit / abort phases of a distributed transaction.
    /// </para>
    /// </summary>
    public interface IDsmHandler
    {
        /// <summary>
        /// Returns <c>true</c> when this handler is responsible for the given
        /// <paramref name="op"/> type.
        /// </summary>
        bool CanHandle(NodeOpType op);

        /// <summary>
        /// Performs any async preparation work required before committing
        /// <paramref name="cmd"/>.  Return <c>null</c> on success or an error
        /// string on failure.  Must not mutate ECS state.
        /// </summary>
        Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct);

        /// <summary>
        /// Commits the previously prepared command.  Called from the main thread
        /// (inside <c>Tick()</c>).  May mutate ECS state via <paramref name="repo"/>.
        /// <paramref name="repo"/> is <c>null</c> for no-ECS subsystems (IOS, CGF skeleton).
        /// </summary>
        void Commit(NodeOpCommand cmd, EntityRepository? repo);

        /// <summary>
        /// Aborts the previously prepared command — rolls back any resources
        /// allocated during <see cref="PrepareAsync"/>.
        /// <paramref name="repo"/> is <c>null</c> for no-ECS subsystems (IOS, CGF skeleton).
        /// </summary>
        void Abort(NodeOpCommand cmd, EntityRepository? repo);
    }
}

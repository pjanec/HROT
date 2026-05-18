using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Implemented by each per-subsystem component that participates in the
    /// Cluster State Machine two-phase-commit protocol.
    ///
    /// <para>
    /// The <c>ClusterSlave</c> calls handlers in sequence during the prepare /
    /// commit / abort phases of a distributed transaction.
    /// </para>
    /// </summary>
    public interface IClusterStateHandler
    {
        /// <summary>
        /// Returns <c>true</c> when this handler is responsible for the given
        /// <paramref name="operation"/>.
        /// </summary>
        bool CanHandle(NodeOpType operation);

        /// <summary>
        /// Returns <c>true</c> when this handler is responsible for the given
        /// <paramref name="intent"/>. By default this forwards to
        /// <see cref="CanHandle(NodeOpType)"/>.
        /// </summary>
        bool CanHandle(ExecuteNodeOpIntent intent) => CanHandle(intent.Operation);

        /// <summary>
        /// Performs any async preparation work required before committing the
        /// <paramref name="intent"/>.  Returns a typed result object on success
        /// (or <c>null</c>).  Must not mutate ECS state.
        /// </summary>
        Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct);

        /// <summary>
        /// Commits the previously prepared intent.  Called from the main thread
        /// (inside <c>Tick()</c>).  May mutate ECS state via <paramref name="repo"/>.
        /// <paramref name="repo"/> is <c>null</c> for no-ECS subsystems (ExCon, CGF skeleton).
        /// </summary>
        void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo);

        /// <summary>
        /// Aborts the previously prepared intent — rolls back any resources
        /// allocated during <see cref="PrepareAsync"/>.
        /// <paramref name="repo"/> is <c>null</c> for no-ECS subsystems (ExCon, CGF skeleton).
        /// </summary>
        void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo);
    }
}

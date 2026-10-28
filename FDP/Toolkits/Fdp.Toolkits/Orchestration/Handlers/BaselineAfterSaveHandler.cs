using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replay;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// ⭐⭐ <c>CE-4093</c> (AQ108 D20) — the node's BASELINE after a completed save round: wraps the host's scenario-save handler and, when the
    /// node's part of the round is done (<see cref="IClusterStateHandler.Commit"/> — the slice is written), takes the world's content hash as the
    /// new baseline ("unchanged" from now on, until something is edited).
    /// <para>⭐ A wrapper, built in the ONE place the save handler is registered (<c>EcsHostClusterHandlers.Compose</c>) — ⛔ not an optional
    /// constructor argument on <c>HrotScenarioSaveHandler</c>, which each host builds on its own and could leave unset (the silent-default rule).
    /// It changes nothing about the save: every member forwards.</para>
    /// <para>⚠ It is the NODE's part: a master that fails the round later (the slice pull or the merge) must itself treat the scenario as unsaved —
    /// a node cannot know.</para>
    /// </summary>
    public sealed class BaselineAfterSaveHandler : IClusterStateHandler
    {
        private readonly IClusterStateHandler _inner;
        private readonly WorldChangeCheck _check;
        private readonly EntityRepository _world;

        /// <exception cref="ArgumentException"><paramref name="inner"/> also takes part in a protocol this wrapper does not forward
        /// (<see cref="IClusterStateUpdateObserver"/>, <see cref="ITickableClusterStateHandler"/>, <see cref="IIdleResetHandler"/>): wrapping it
        /// would silently drop that part — extend the wrapper first.</exception>
        public BaselineAfterSaveHandler(IClusterStateHandler inner, WorldChangeCheck check, EntityRepository world)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _check = check ?? throw new ArgumentNullException(nameof(check));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (inner is IClusterStateUpdateObserver || inner is ITickableClusterStateHandler || inner is IIdleResetHandler)
                throw new ArgumentException($"{inner.GetType().Name} takes part in a cluster-slave protocol the baseline wrapper does not forward", nameof(inner));
        }

        /// <summary>The wrapped handler (rails and diagnostics).</summary>
        public IClusterStateHandler Inner => _inner;

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => _inner.CanHandle(operation);

        /// <inheritdoc />
        public bool CanHandle(ExecuteNodeOpIntent intent) => _inner.CanHandle(intent);

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct) => _inner.PrepareAsync(intent, ct);

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _inner.Commit(intent, repo);
            // the inner handler claims only a scenario save; its commit means this node's slice is on disk
            _check.TakeBaseline(_world);
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) => _inner.Abort(intent, repo);
    }
}

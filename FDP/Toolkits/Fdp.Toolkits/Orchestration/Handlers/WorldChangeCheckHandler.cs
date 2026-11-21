using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replay;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// ⭐⭐ <c>CE-4093</c> (AQ108 D20) — the node's half of the CHECK ROUND: <see cref="NodeOpType.CheckUnsaved"/> asks "is your whole world
    /// different from your baseline?" and the node answers with ONE string — <see cref="Changed"/>, <see cref="Unchanged"/> or
    /// <see cref="NoBaseline"/> (none was taken: nothing says the world is saved, so the master treats it as changed). The answer travels as the
    /// op's <c>ResultPayload</c> (a JSON string on the wire, the same mechanism as <c>ExportCheckpoint</c>'s path) and lands in the master's
    /// <c>NodeResponses[nodeId][CheckUnsaved]</c> for an <c>INodeResponseAggregator</c>; read it with <see cref="Parse"/>.
    /// <para>Nothing is mutated and nothing is stored but the answer: the capture runs in <see cref="PrepareAsync"/> on the frame thread
    /// (<c>ClusterSlave</c> dispatches there), so the world is not written meanwhile. ⛔ Never asked periodically — only by a round the master
    /// starts. 📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md D20.</para>
    /// </summary>
    public sealed class WorldChangeCheckHandler : IClusterStateHandler
    {
        /// <summary>The world differs from the node's baseline.</summary>
        public const string Changed = "changed";
        /// <summary>The world's content equals the baseline's.</summary>
        public const string Unchanged = "unchanged";
        /// <summary>The node took no baseline (its load never committed here, or nothing saved) — the master counts it as changed.</summary>
        public const string NoBaseline = "no-baseline";

        private readonly WorldChangeCheck _check;
        private readonly EntityRepository _world;

        public WorldChangeCheckHandler(WorldChangeCheck check, EntityRepository world)
        {
            _check = check ?? throw new ArgumentNullException(nameof(check));
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.CheckUnsaved;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            string answer = !_check.HasBaseline ? NoBaseline : _check.HasChanged(_world) ? Changed : Unchanged;
            FdpLog<WorldChangeCheckHandler>.Info("[CheckUnsaved] this node's world is: {0}.", answer);
            return Task.FromResult<object?>(answer);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

        /// <summary>
        /// ⭐ Reads a node's response as the master's aggregator stored it — the plain word or the JSON string the wire carries
        /// (<c>"changed"</c>). Returns one of the three constants, or <c>null</c> for an empty / unknown answer (a silent node: the
        /// aggregator decides; the safe reading is "changed").
        /// </summary>
        public static string? Parse(string? response)
        {
            if (string.IsNullOrWhiteSpace(response)) return null;
            var word = response.Trim().Trim('"');
            return word is Changed or Unchanged or NoBaseline ? word : null;
        }

        /// <summary>The safe reading of <see cref="Parse"/>'s result: only an explicit <see cref="Unchanged"/> means saved.</summary>
        public static bool MeansChanged(string? response) => Parse(response) != Unchanged;
    }
}

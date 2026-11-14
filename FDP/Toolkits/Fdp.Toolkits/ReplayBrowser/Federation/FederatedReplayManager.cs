using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// Exception thrown by <see cref="FederatedReplayManager.LoadGroup"/> when
    /// the provided recording group fails validation.
    /// </summary>
    public sealed class LoadGroupException : Exception
    {
        /// <summary>Creates a <see cref="LoadGroupException"/> with the given reason message.</summary>
        public LoadGroupException(string message) : base(message) { }
    }

    /// <summary>
    /// Coordinates loading and time-synchronized seeking of a multi-node federated replay group.
    /// Each node corresponds to one <c>.fdp</c> recording file produced by a single
    /// distributed exercise participant.
    /// </summary>
    public sealed class FederatedReplayManager : IDisposable
    {
        private readonly Dictionary<int, ReplayBrowserContext> _contexts;
        private readonly Dictionary<int, long> _nodeOffsets;
        private bool _disposed;
        private ReplayBrowserContext[] _ctxArr = Array.Empty<ReplayBrowserContext>();
        private int[] _idArr = Array.Empty<int>();

        /// <summary>⭐ G-7 S4 — the nodes as ARRAYS (ascending node id), so the per-frame merge walks them without an enumerator (0 B, R-2084). Fixed once the group is loaded.</summary>
        internal int NodeCount => _ctxArr.Length;
        internal int NodeIdAt(int i) => _idArr[i];
        internal ReplayBrowserContext ContextAt(int i) => _ctxArr[i];

        /// <summary>Per-node <see cref="ReplayBrowserContext"/> instances keyed by NodeId.</summary>
        public IReadOnlyDictionary<int, ReplayBrowserContext> Contexts { get; }

        /// <summary>Distributed exercise identifier shared by all loaded nodes.</summary>
        public ExerciseId ExerciseId { get; }

        /// <summary>Base wall-clock tick origin applied to every node seek.</summary>
        public long BaseWallTicks { get; private set; }

        /// <summary>Per-node wall-clock tick offsets applied on top of <see cref="BaseWallTicks"/>.</summary>
        public IReadOnlyDictionary<int, long> NodeOffsets { get; }

        /// <summary>
        /// ⭐ G-7 S5 (I7) — the PRIMARY node: the one whose frames drive the group's clock, the timeline and Recording info (and a part's source in the editor). Defaults to the lowest NodeId.
        /// ⛔ SUPERSEDED: "the canonical source of local entities" — the merge now takes the local (no network id) entities of EVERY node whose <see cref="IsLocalEntitiesShown"/> is on.
        /// (The name is kept: ~15 callers and rails; it no longer decides which locals show.)
        /// </summary>
        public NodeId LocalEntitiesProviderNodeId { get; private set; }

        private readonly HashSet<int> _localsHidden = new();   // nodes whose local entities are NOT merged (default: none — every node's are shown)

        /// <summary>⭐ G-7 S5 (I7) — are the local (no network id) entities of <paramref name="nodeId"/> merged into the world? Default: yes, every node's.</summary>
        public bool IsLocalEntitiesShown(int nodeId) => !_localsHidden.Contains(nodeId);

        /// <summary>
        /// ⭐ G-7 S5 (I7) — the Source menu's <i>Local entities shown from</i> checkbox: shows or hides node <paramref name="nodeId"/>'s local entities in the merged world.
        /// Fires <see cref="OnTimeChanged"/> (the hosts re-merge) but does NOT seek.
        /// </summary>
        public void SetLocalEntitiesShown(int nodeId, bool shown)
        {
            ThrowIfDisposed();
            if (!_contexts.ContainsKey(nodeId))
                throw new ArgumentOutOfRangeException(nameof(nodeId), $"NodeId {nodeId} is not in the loaded group.");
            if (shown ? !_localsHidden.Remove(nodeId) : !_localsHidden.Add(nodeId)) return;
            OnTimeChanged?.Invoke();
        }

        /// <summary>Fired after every seek or after a local-entities-provider change.</summary>
        public event Action? OnTimeChanged;

        private FederatedReplayManager(
            Dictionary<int, ReplayBrowserContext> contexts,
            Guid exerciseId,
            int lowestNodeId)
        {
            _contexts = contexts;
            _nodeOffsets = new Dictionary<int, long>();
            Contexts = _contexts;
            NodeOffsets = _nodeOffsets;
            ExerciseId = (Fdp.Core.ExerciseId)exerciseId;
            LocalEntitiesProviderNodeId = (Fdp.Core.NodeId)(lowestNodeId);
            _idArr = _contexts.Keys.OrderBy(k => k).ToArray();
            _ctxArr = new ReplayBrowserContext[_idArr.Length];
            for (int i = 0; i < _idArr.Length; i++) _ctxArr[i] = _contexts[_idArr[i]];
        }

        /// <summary>
        /// Loads a federated replay group from the given <c>.fdp</c> file paths.
        /// Each path must have a corresponding <c>.meta.json</c> sidecar.
        /// </summary>
        /// <param name="paths">Paths to the <c>.fdp</c> files that form the group.</param>
        /// <returns>A fully initialised manager with one context per path.</returns>
        /// <exception cref="LoadGroupException">
        /// Thrown when any of the following validation rules fails:
        /// <list type="bullet">
        /// <item><description>A sidecar has <c>ExerciseId == Guid.Empty</c> ("unknown exercise").</description></item>
        /// <item><description>Not all sidecars share the same <c>ExerciseId</c> ("exercise mismatch").</description></item>
        /// <item><description>Two sidecars share the same <c>NodeId</c> ("duplicate NodeId {id}").</description></item>
        /// </list>
        /// Any already-created contexts are disposed before the exception propagates.
        /// </exception>
        public static FederatedReplayManager LoadGroup(string[] paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            var loaded = new Dictionary<int, ReplayBrowserContext>();
            Guid? exerciseId = null;

            try
            {
                foreach (var path in paths)
                {
                    var metaPath = path + ".meta.json";
                    var json = File.ReadAllText(metaPath);
                    var meta = MetadataSerializer.Deserialize(json);

                    if (meta.ExerciseId == Guid.Empty)
                        throw new LoadGroupException("unknown exercise");

                    if (exerciseId == null)
                        exerciseId = meta.ExerciseId;
                    else if (exerciseId.Value != meta.ExerciseId)
                        throw new LoadGroupException("exercise mismatch");

                    if (loaded.ContainsKey(meta.NodeId))
                        throw new LoadGroupException($"duplicate NodeId {meta.NodeId}");

                    var ctx = new ReplayBrowserContext();
                    loaded[meta.NodeId] = ctx;
                    ctx.LoadRecording(path);
                }

                int lowestNodeId = loaded.Keys.Min();
                return new FederatedReplayManager(loaded, exerciseId!.Value, lowestNodeId);
            }
            catch
            {
                foreach (var ctx in loaded.Values)
                    ctx.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Sets the base wall-clock tick origin and seeks all contexts to the new position.
        /// Fires <see cref="OnTimeChanged"/>.
        /// </summary>
        public void SetBaseWallTicks(long ticks)
        {
            ThrowIfDisposed();
            BaseWallTicks = ticks;
            SeekAll();
        }

        /// <summary>
        /// Sets the per-node wall-clock offset for the given node and seeks all contexts.
        /// Fires <see cref="OnTimeChanged"/>.
        /// </summary>
        public void SetNodeOffset(int nodeId, long offsetTicks)
        {
            ThrowIfDisposed();
            if (!_contexts.ContainsKey(nodeId))
                throw new ArgumentOutOfRangeException(
                    nameof(nodeId), $"Node {nodeId} is not loaded in this FederatedReplayManager.");
            _nodeOffsets[nodeId] = offsetTicks;
            SeekAll();
        }

        /// <summary>
        /// Changes the PRIMARY node (the clock / timeline / Recording-info node — see <see cref="LocalEntitiesProviderNodeId"/>) to the given node.
        /// Fires <see cref="OnTimeChanged"/> but does NOT seek.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="nodeId"/> is not present in the loaded group.
        /// </exception>
        public void SetLocalEntitiesProvider(int nodeId)
        {
            ThrowIfDisposed();
            if (!_contexts.ContainsKey(nodeId))
                throw new ArgumentOutOfRangeException(
                    nameof(nodeId), $"NodeId {nodeId} is not in the loaded group.");
            LocalEntitiesProviderNodeId = (Fdp.Core.NodeId)(nodeId);
            OnTimeChanged?.Invoke();
        }

        /// <summary>
        /// Seeks every context to <c>BaseWallTicks + NodeOffset[nodeId]</c> and fires
        /// <see cref="OnTimeChanged"/>.
        /// </summary>
        public void SeekAll()
        {
            ThrowIfDisposed();
            foreach (var (nodeId, ctx) in _contexts)
            {
                if (ctx.Playback == null) continue;
                long off = _nodeOffsets.TryGetValue(nodeId, out long v) ? v : 0L;
                ctx.Playback.SeekToWallClockTicks(ctx.SandboxRepo, BaseWallTicks + off);
            }
            OnTimeChanged?.Invoke();
        }

        /// <summary>
        /// Advances every loaded context by one frame using sequential stepping.
        /// Fires <see cref="OnTimeChanged"/>.
        /// </summary>
        public void StepForwardAll()
        {
            ThrowIfDisposed();
            foreach (var ctx in _contexts.Values)
            {
                if (ctx.Playback != null && !ctx.Playback.IsAtEnd)
                    ctx.StepForward(suppressHistory: true);
            }

            if (_contexts.TryGetValue(LocalEntitiesProviderNodeId, out var primaryCtx) && primaryCtx.Playback != null)
                BaseWallTicks = primaryCtx.Playback.GetFrameMetadata(primaryCtx.CurrentFrame).WallClockTicks;

            OnTimeChanged?.Invoke();
        }

        /// <summary>
        /// Rewinds every loaded context by one frame using sequential stepping.
        /// Fires <see cref="OnTimeChanged"/>.
        /// </summary>
        public void StepBackwardAll()
        {
            ThrowIfDisposed();
            foreach (var ctx in _contexts.Values)
            {
                if (ctx.Playback != null && ctx.CurrentFrame > 0)
                    ctx.StepBackward(suppressHistory: true);
            }

            if (_contexts.TryGetValue(LocalEntitiesProviderNodeId, out var primaryCtx) && primaryCtx.Playback != null)
                BaseWallTicks = primaryCtx.Playback.GetFrameMetadata(primaryCtx.CurrentFrame).WallClockTicks;

            OnTimeChanged?.Invoke();
        }

        /// <summary>
        /// Disposes all owned <see cref="ReplayBrowserContext"/> instances.
        /// Double-dispose is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var ctx in _contexts.Values)
                ctx.Dispose();
            _contexts.Clear();
            _nodeOffsets.Clear();
            _ctxArr = Array.Empty<ReplayBrowserContext>();
            _idArr = Array.Empty<int>();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FederatedReplayManager));
        }
    }
}

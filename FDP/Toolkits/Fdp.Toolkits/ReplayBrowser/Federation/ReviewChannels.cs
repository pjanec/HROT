using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    // ⭐ R-4220 / CE-4496 (docs/DESIGN_Threaded_Review_Source.md §1.1) — what the review UI holds: a VIEW (the published read model + the request it asks through) and COMMANDS.
    //   ⛔ No review UI type holds a FederatedReplayManager (acceptance ④). InlineReviewChannel serves the standalone Replay Browser and the editor with the switch OFF
    //   (same thread); ThreadedReviewChannel (Hrot.Editor) serves mode 3 (commands as sim jobs, the model carried by the staging).

    /// <summary>⭐ What the review UI asks of the source. Applied on the source's thread (at once inline; as a sim job in mode 3).</summary>
    public interface IReviewCommands
    {
        void SelectMerged();
        void SelectPart(int node);
        void SeekFrame(int frame);
        void StepFrame(int delta);
        void SetNodeOffsetTicks(int node, long offsetTicks);
        void SetBaseWallTicks(long ticks);
        void SetLocalEntitiesShown(int node, bool shown);
        void SetLocalEntitiesProvider(int node);
    }

    /// <summary>⭐ What the review UI reads: the newest published model, and the collector its panels ask for views through.</summary>
    public interface IReviewView
    {
        ReviewReadModel Model { get; }
        ReviewRequestCollector Requests { get; }
    }

    /// <summary>Both halves — what a review panel is given.</summary>
    public interface IReviewChannel : IReviewView, IReviewCommands
    {
        /// <summary>Once a UI frame, after the panels drew: the asks become the request (when they changed) and ride to the source.</summary>
        void EndFrame();
    }

    /// <summary>
    /// ⭐ The UI side of <see cref="ReviewViewRequest"/>: panels ask for the views they draw (By node, the watch) every frame they draw them; once a frame
    /// <see cref="EndFrame"/> makes a NEW request only when the asked set changed — so it rides to the source only then (§2). A key not asked for
    /// <see cref="KeepFrames"/> frames is dropped. 0 B in the steady state.
    /// </summary>
    public sealed class ReviewRequestCollector
    {
        /// <summary>How long an unasked key stays in the request (a panel drawn every other frame does not churn it).</summary>
        public const int KeepFrames = 60;

        private readonly List<((long, Type) Key, int Frame)> _byNode = new();
        private readonly List<(ReviewWatchKey Key, int Frame)> _watch = new();
        private int _frame;
        private bool _dirty;
        private int _version;

        /// <summary>The newest request made.</summary>
        public ReviewViewRequest Current { get; private set; } = ReviewViewRequest.Empty;

        public void AskByNode(long netId, Type component)
        {
            if (netId <= 0) return;
            for (int i = 0; i < _byNode.Count; i++)
                if (_byNode[i].Key.Item1 == netId && _byNode[i].Key.Item2 == component) { _byNode[i] = (_byNode[i].Key, _frame); return; }
            _byNode.Add(((netId, component), _frame)); _dirty = true;
        }

        public void AskWatch(long netId, ReplicaWatchItem item)
        {
            if (netId <= 0) return;
            for (int i = 0; i < _watch.Count; i++)
                if (_watch[i].Key.NetId == netId && _watch[i].Key.Item.Path == item.Path && _watch[i].Key.Item.SubIndex == item.SubIndex)
                {
                    if (ReferenceEquals(_watch[i].Key.Item, item) || _watch[i].Key.Item == item) { _watch[i] = (_watch[i].Key, _frame); return; }
                    _watch[i] = (new ReviewWatchKey(netId, item), _frame); _dirty = true; return;   // the same attribute, a new tolerance / colour: the view is recomputed
                }
            _watch.Add((new ReviewWatchKey(netId, item), _frame)); _dirty = true;
        }

        /// <summary>Once a UI frame: drops stale keys; returns a NEW request when the set changed, else null (0 B).</summary>
        public ReviewViewRequest? EndFrame()
        {
            for (int i = _byNode.Count - 1; i >= 0; i--) if (_frame - _byNode[i].Frame > KeepFrames) { _byNode.RemoveAt(i); _dirty = true; }
            for (int i = _watch.Count - 1; i >= 0; i--) if (_frame - _watch[i].Frame > KeepFrames) { _watch.RemoveAt(i); _dirty = true; }
            _frame++;
            if (!_dirty) return null;
            _dirty = false;
            var byNode = new (long, Type)[_byNode.Count];
            for (int i = 0; i < byNode.Length; i++) byNode[i] = _byNode[i].Key;
            var watch = new ReviewWatchKey[_watch.Count];
            for (int i = 0; i < watch.Length; i++) watch[i] = _watch[i].Key;
            return Current = new ReviewViewRequest(++_version, byNode, watch);
        }
    }

    /// <summary>
    /// ⭐ The review channel on ONE thread (the standalone Replay Browser; the editor with the switch off): commands apply at once; <see cref="Model"/> is built by
    /// the ONE builder on read and is the same instance while nothing changed (0 B).
    /// </summary>
    public sealed class InlineReviewChannel : IReviewChannel
    {
        private readonly IReviewHost _host;
        private readonly ReviewModelBuilder _builder;
        private ReviewReadModel? _model;

        public InlineReviewChannel(IReviewHost host, ReviewModelBuilder builder)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>A channel over a bare manager (what the standalone Replay Browser and the panels' rails use): Merged / one node by <paramref name="kind"/>, names by <paramref name="nameOf"/>.</summary>
        public static InlineReviewChannel OverManager(Func<FederatedReplayManager?> manager, Func<ReviewKind>? kind = null, Func<int, string>? nameOf = null, ReplicaViews? views = null)
            => new(new ManagerReviewHost(manager, kind, nameOf), new ReviewModelBuilder(views));

        /// <inheritdoc cref="OverManager(Func{FederatedReplayManager?}, Func{ReviewKind}?, Func{int, string}?, ReplicaViews?)"/>
        public static InlineReviewChannel OverManager(FederatedReplayManager manager, Func<int, string>? nameOf = null) => OverManager(() => manager, null, nameOf);

        public ReviewReadModel Model => _model = _builder.Build(_host, Requests.Current, _model);
        public ReviewRequestCollector Requests { get; } = new();

        /// <summary>Once a UI frame: the panels' asks become the request (when they changed).</summary>
        public void EndFrame() => Requests.EndFrame();

        public void SelectMerged() => _host.SelectMerged();
        public void SelectPart(int node) => _host.SelectPart(node);
        public void SeekFrame(int frame) => _host.SeekFrame(frame);
        public void StepFrame(int delta) => _host.StepFrame(delta);
        public void SetNodeOffsetTicks(int node, long offsetTicks) => _host.SetNodeOffsetTicks(node, offsetTicks);
        public void SetBaseWallTicks(long ticks) => _host.SetBaseWallTicks(ticks);
        public void SetLocalEntitiesShown(int node, bool shown) => _host.SetLocalEntitiesShown(node, shown);
        public void SetLocalEntitiesProvider(int node) => _host.SetLocalEntitiesProvider(node);
    }

    /// <summary>
    /// ⭐ CE-4498 R-4 (docs/DESIGN_Threaded_Review_Source.md §1.2) — the SIM side of the threaded review: holds the request the UI last sent and the model last published; once a sim step
    /// (after the review source's <c>Update</c>) <see cref="PublishIfChanged"/> builds through the ONE builder and returns the model only when it is a NEW one — the caller carries it to the UI
    /// with the staging (the D-9 readout pattern). 0 B when nothing changed.
    /// </summary>
    public sealed class ReviewModelPublisher
    {
        private readonly ReviewModelBuilder _builder;
        private ReviewReadModel? _last;

        public ReviewModelPublisher(ReviewModelBuilder builder) => _builder = builder ?? throw new ArgumentNullException(nameof(builder));

        /// <summary>The request the UI last sent (set by a sim job; read by the next build).</summary>
        public ReviewViewRequest Request { get; set; } = ReviewViewRequest.Empty;

        /// <summary>The model last published (sim thread).</summary>
        public ReviewReadModel? Last => _last;

        /// <summary>The model for <paramref name="host"/> now, when it differs from the one last published; else null.</summary>
        public ReviewReadModel? PublishIfChanged(IReviewHost host)
        {
            var m = _builder.Build(host, Request, _last);
            if (ReferenceEquals(m, _last)) return null;
            return _last = m;
        }
    }

    /// <summary>
    /// ⭐ CE-4498 R-4 (docs/DESIGN_Threaded_Review_Source.md §1.1, §1.2) — <b>the review channel across threads</b> (the threaded editor, mode 3): a command becomes a job on the source's thread
    /// (<paramref name="post"/>); <see cref="Model"/> is the newest model the source published, delivered on the UI thread by <see cref="Apply"/> (carried by the staging); the panels' request rides
    /// to the source only when it changed (<see cref="EndFrame"/>). ⛔ Nothing here reads the source or its manager: the UI thread sees only immutable models.
    /// </summary>
    public sealed class ThreadedReviewChannel : IReviewChannel
    {
        private readonly IReviewCommands _source;
        private readonly ReviewModelPublisher _publisher;
        private readonly Action<Action> _post;
        private volatile ReviewReadModel _model = ReviewReadModel.Empty;

        /// <param name="source">The source's commands — invoked ONLY inside a posted job (on its thread).</param>
        /// <param name="publisher">The sim side's publisher (its request is set by a posted job).</param>
        /// <param name="post">Runs an action on the source's thread, in order (the sim job queue).</param>
        public ThreadedReviewChannel(IReviewCommands source, ReviewModelPublisher publisher, Action<Action> post)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _post = post ?? throw new ArgumentNullException(nameof(post));
        }

        public ReviewReadModel Model => _model;
        public ReviewRequestCollector Requests { get; } = new();

        /// <summary>UI thread: the model the source published (carried by the staging, applied in order with the pull).</summary>
        public void Apply(ReviewReadModel model) { if (model != null) _model = model; }

        /// <summary>Commands sent so far (a rail's counter).</summary>
        public int Posted { get; private set; }

        public void EndFrame()
        {
            if (Requests.EndFrame() is { } request) SendRequest(request);
        }

        // ⚠ its own method: a lambda capturing `request` inside EndFrame makes the compiler allocate the closure at EndFrame's ENTRY — 32 B every UI frame, posted or not (measured, BUG-3)
        private void SendRequest(ReviewViewRequest request) => Post(() => _publisher.Request = request);

        private void Post(Action a) { Posted++; _post(a); }

        public void SelectMerged() => Post(() => _source.SelectMerged());
        public void SelectPart(int node) => Post(() => _source.SelectPart(node));
        public void SeekFrame(int frame) => Post(() => _source.SeekFrame(frame));
        public void StepFrame(int delta) => Post(() => _source.StepFrame(delta));
        public void SetNodeOffsetTicks(int node, long offsetTicks) => Post(() => _source.SetNodeOffsetTicks(node, offsetTicks));
        public void SetBaseWallTicks(long ticks) => Post(() => _source.SetBaseWallTicks(ticks));
        public void SetLocalEntitiesShown(int node, bool shown) => Post(() => _source.SetLocalEntitiesShown(node, shown));
        public void SetLocalEntitiesProvider(int node) => Post(() => _source.SetLocalEntitiesProvider(node));
    }

    /// <summary>
    /// ⭐ <see cref="IReviewHost"/> over a bare <see cref="FederatedReplayManager"/> — the standalone Replay Browser's review (no ReviewSource there): the facts come from the
    /// manager's primary playback; Select Merged / Part are the host's view-mode switch (<paramref name="selectMerged"/> / <paramref name="selectPart"/>).
    /// </summary>
    public sealed class ManagerReviewHost : IReviewHost
    {
        private readonly Func<FederatedReplayManager?> _manager;
        private readonly Func<ReviewKind> _kind;
        private readonly Func<int, string> _nameOf;
        private readonly Action? _selectMerged;
        private readonly Action<int>? _selectPart;
        private readonly Dictionary<int, string> _names = new(), _parts = new();

        public ManagerReviewHost(Func<FederatedReplayManager?> manager, Func<ReviewKind>? kind = null, Func<int, string>? nameOf = null, Action? selectMerged = null, Action<int>? selectPart = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _kind = kind ?? (() => ReviewKind.Merged);
            _nameOf = nameOf ?? (n => "Node " + n);
            _selectMerged = selectMerged;
            _selectPart = selectPart;
        }

        public FederatedReplayManager? Manager => _manager();

        public ReviewHostFacts Facts
        {
            get
            {
                var m = _manager();
                if (m is null) return default;
                var pb = m.Contexts.TryGetValue(m.LocalEntitiesProviderNodeId, out var c) ? c : null;
                return new ReviewHostFacts(_kind(), true, false, null, m.ExerciseId.Value, (int)m.LocalEntitiesProviderNodeId,
                    pb?.CurrentFrame ?? -1, pb?.Playback?.TotalFrames ?? 0, false, null, null, null, 0);
            }
        }

        public string NameOf(int node) { if (!_names.TryGetValue(node, out var n)) _names[node] = n = _nameOf(node); return n; }
        public string RoleKeyOf(int node) => "node/other";
        public string PartLabel(int node) { if (!_parts.TryGetValue(node, out var n)) _parts[node] = n = NameOf(node) + "'s part"; return n; }

        public void SelectMerged() => _selectMerged?.Invoke();
        public void SelectPart(int node) => _selectPart?.Invoke(node);

        public void SeekFrame(int frame)
        {
            var m = _manager();
            if (m is null || !m.Contexts.TryGetValue(m.LocalEntitiesProviderNodeId, out var c) || c.Playback is not { TotalFrames: > 0 } pb) return;
            m.SetBaseWallTicks(pb.GetFrameMetadata(Math.Clamp(frame, 0, pb.TotalFrames - 1)).WallClockTicks);
        }

        public void StepFrame(int delta)
        {
            var m = _manager();
            if (m is null || !m.Contexts.TryGetValue(m.LocalEntitiesProviderNodeId, out var c) || c.Playback is not { } pb) return;
            if (delta > 0) { if (c.CurrentFrame + 1 < pb.TotalFrames) m.StepForwardAll(); }
            else if (delta < 0 && c.CurrentFrame > 0) m.StepBackwardAll();
        }

        public void SetNodeOffsetTicks(int node, long offsetTicks) => _manager()?.SetNodeOffset(node, offsetTicks);
        public void SetBaseWallTicks(long ticks) => _manager()?.SetBaseWallTicks(ticks);
        public void SetLocalEntitiesShown(int node, bool shown) => _manager()?.SetLocalEntitiesShown(node, shown);
        public void SetLocalEntitiesProvider(int node) => _manager()?.SetLocalEntitiesProvider(node);
    }
}

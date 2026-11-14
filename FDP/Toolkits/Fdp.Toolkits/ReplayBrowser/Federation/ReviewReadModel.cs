using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    // ⭐ R-4220 / CE-4495 (docs/DESIGN_Threaded_Review_Source.md §1.1) — the review's READ MODEL: an immutable picture of the review source the UI draws from,
    //   built by ONE builder on the thread that owns the source (the sim thread in the threaded editor; the UI thread in the standalone Replay Browser and with the
    //   switch off). The panels never hold a FederatedReplayManager, a context or a sandbox world — only this model and IReviewCommands.

    /// <summary>What the review shows.</summary>
    public enum ReviewKind { None, Merged, Part }

    /// <summary>
    /// What the host knows that the manager does not — filled by the host every build (a struct: 0 B). <paramref name="Key"/> is the host's own change key
    /// (anything else it shows that may change — a name source, a pending open); the builder folds it into <see cref="ReviewReadModel.Version"/>.
    /// </summary>
    public readonly record struct ReviewHostFacts(
        ReviewKind Kind, bool IsOpen, bool IsPending, string? Failure, Guid? ExerciseId, int PartNode,
        int CurrentFrame, int TotalFrames, bool IsPlaying, string? StatusText, string? FaceLabel, string? FaceIconKey, int Key);

    /// <summary>
    /// ⭐ The review source as the builder and the commands see it — implemented by the editor's <c>ReviewSource</c> and the standalone Replay Browser.
    /// Everything here runs on the thread that owns the source.
    /// </summary>
    public interface IReviewHost : IReviewCommands
    {
        /// <summary>The group's manager, or null when no group is open. ⛔ Read only by the builder / the host's own thread.</summary>
        FederatedReplayManager? Manager { get; }
        ReviewHostFacts Facts { get; }
        /// <summary>A participant's name (cached by the host).</summary>
        string NameOf(int node);
        /// <summary>A participant's role icon key (<c>node/cgf</c> …).</summary>
        string RoleKeyOf(int node);
        /// <summary>A participant's Source-menu row (<c>"CGF-1's part"</c>).</summary>
        string PartLabel(int node);
        // the commands (IReviewCommands) — applied on the host's thread

        /// <summary>⭐ CE-4498 — the merged world's LOCAL entities → the node each came from (Details' <i>"local to &lt;node&gt;"</i>), an immutable snapshot the host replaces when it changes; null when it has none.</summary>
        IReadOnlyDictionary<Fdp.Core.Entity, int>? LocalOrigins => null;
    }

    /// <summary>One participant of the open group, as drawn.</summary>
    public sealed record ReviewParticipant(int Node, string Name, string RoleKey, string PartLabel, string? Path, long OffsetTicks, bool LocalShown, int CurrentFrame,
        int TotalFrames = 0, double DurationSeconds = 0)
    {
        public double OffsetMs => (double)OffsetTicks / TimeSpan.TicksPerMillisecond;
    }

    /// <summary>One watched attribute of a unit — the replica watch's key.</summary>
    public readonly record struct ReviewWatchKey(long NetId, ReplicaWatchItem Item);

    /// <summary>
    /// ⭐ What the UI asks the builder to compute beyond the always-present facts: the By-node views (a unit × a component shown in Details) and the watch's per-node views.
    /// Immutable; the UI makes a new one only when its content changes (<see cref="Version"/> moves), so it rides to the sim thread only then (§2).
    /// </summary>
    public sealed record ReviewViewRequest(int Version, IReadOnlyList<(long NetId, Type Component)> ByNode, IReadOnlyList<ReviewWatchKey> Watch)
    {
        public static readonly ReviewViewRequest Empty = new(0, Array.Empty<(long, Type)>(), Array.Empty<ReviewWatchKey>());
    }

    /// <summary>
    /// ⭐ The immutable review read model (§1.1). Published whole; a newer one replaces it. <see cref="Version"/> moves whenever anything in it would.
    /// </summary>
    public sealed record ReviewReadModel(
        long Version,
        ReviewHostFacts Facts,
        IReadOnlyList<ReviewParticipant> Participants,
        int PrimaryNode,
        long BaseWallTicks,
        bool HasNonZeroOffset,
        long ReplicaStateKey,
        ReviewViewRequest Request,
        IReadOnlyList<ReplicaView?> ByNode,
        IReadOnlyList<ReplicaView?> Watch,
        IReadOnlyDictionary<int, long> NodeOffsets,
        string[] ParticipantPaths,
        long[]? PrimaryFrameWallTicks = null,
        double PrimarySimSeconds = 0,
        double[]? PrimaryFrameSimSeconds = null,
        IReadOnlyDictionary<Fdp.Core.Entity, int>? LocalOrigins = null)
    {
        /// <summary>⭐ CE-4498 — the recorded sim seconds of the primary's <paramref name="frame"/> (the transport's tooltip), NaN when the file has none there.</summary>
        public double SimSecondsAt(int frame)
        {
            var t = PrimaryFrameSimSeconds;
            if (t is null || t.Length == 0) return double.NaN;
            return t[Math.Clamp(frame, 0, t.Length - 1)];
        }

        /// <summary>⭐ CE-4498 — the node a LOCAL entity of the merged world came from (<see cref="IReviewHost.LocalOrigins"/>).</summary>
        public bool TryGetLocalNode(Fdp.Core.Entity entity, out int node)
        {
            if (LocalOrigins is { } map && map.TryGetValue(entity, out node)) return true;
            node = 0; return false;
        }

        /// <summary>The primary participant's wall clock at <paramref name="frame"/> (the timeline's axis), or 0.</summary>
        public long WallTicksAt(int frame)
        {
            var t = PrimaryFrameWallTicks;
            if (t is null || t.Length == 0) return 0;
            return t[Math.Clamp(frame, 0, t.Length - 1)];
        }

        /// <summary>The primary participant's row, or null.</summary>
        public ReviewParticipant? Primary => Participant(PrimaryNode);

        public static readonly ReviewReadModel Empty = new(0, default, Array.Empty<ReviewParticipant>(), 0, 0, false, 0, ReviewViewRequest.Empty,
            Array.Empty<ReplicaView?>(), Array.Empty<ReplicaView?>(), new Dictionary<int, long>(), Array.Empty<string>());

        public ReviewKind Kind => Facts.Kind;
        public bool IsOpen => Facts.IsOpen;
        /// <summary>The merged view is shown (By node, the watch lane, the federated search apply).</summary>
        public bool IsMerged => Facts.IsOpen && Facts.Kind == ReviewKind.Merged;
        public int CurrentFrame => Facts.CurrentFrame;
        public int TotalFrames => Facts.TotalFrames;

        /// <summary>The By-node view of <paramref name="netId"/> / <paramref name="component"/>, when the request asked for it (else null — the UI adds it to its next request).</summary>
        public bool TryByNode(long netId, Type component, out ReplicaView? view)
        {
            var keys = Request.ByNode;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].NetId == netId && keys[i].Component == component) { view = i < ByNode.Count ? ByNode[i] : null; return true; }
            view = null; return false;
        }

        /// <summary>The watch view of one attribute of <paramref name="netId"/>, when the request asked for it.</summary>
        public bool TryWatch(long netId, ReplicaWatchItem item, out ReplicaView? view)
        {
            var keys = Request.Watch;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].NetId == netId && keys[i].Item.Path == item.Path && keys[i].Item.SubIndex == item.SubIndex) { view = i < Watch.Count ? Watch[i] : null; return true; }
            view = null; return false;
        }

        /// <summary>The participant row of <paramref name="node"/>, or null.</summary>
        public ReviewParticipant? Participant(int node)
        {
            for (int i = 0; i < Participants.Count; i++) if (Participants[i].Node == node) return Participants[i];
            return null;
        }
    }

    /// <summary>
    /// ⭐ THE builder of <see cref="ReviewReadModel"/> — ONE for every host (R-4215). Reads the host and its manager, computes the requested By-node and watch views
    /// through <see cref="ReplicaViews"/>, and returns the PREVIOUS model when nothing it shows changed (the version key costs 0 B — §2, the <c>ReadIfChanged</c> precedent).
    /// Runs on the thread that owns the host.
    /// </summary>
    public sealed class ReviewModelBuilder
    {
        private readonly ReplicaViews? _views;

        /// <param name="views">The merged view's lens; null when the host has none (no By node / watch views are computed).</param>
        public ReviewModelBuilder(ReplicaViews? views) => _views = views;

        /// <summary>How many models were built (a rail's counter: an unchanged state builds none).</summary>
        public int Builds { get; private set; }

        /// <summary>The model for <paramref name="host"/> now — <paramref name="previous"/> itself when nothing changed.</summary>
        public ReviewReadModel Build(IReviewHost host, ReviewViewRequest request, ReviewReadModel? previous)
        {
            var m = host.Manager;
            var facts = host.Facts;
            var locals = host.LocalOrigins;
            long version = Mix(VersionOf(m, facts, request), RuntimeHelpers.GetHashCode(locals ?? (object)""));
            if (previous is not null && previous.Version == version && ReferenceEquals(previous.Request, request)) return previous;
            Builds++;

            int n = m?.NodeCount ?? 0;
            var parts = new ReviewParticipant[n];
            var offsets = new Dictionary<int, long>(n);
            var paths = new List<string>(n);
            bool nonZero = false;
            for (int i = 0; i < n; i++)
            {
                int node = m!.NodeIdAt(i);
                var ctx = m.ContextAt(i);
                long off = m.NodeOffsets.TryGetValue(node, out long o) ? o : 0L;
                if (off != 0) nonZero = true;
                offsets[node] = off;
                if (ctx.CurrentFdpPath is { } p) paths.Add(p);
                int frames = ctx.Playback?.TotalFrames ?? 0;
                double seconds = frames > 1 ? (ctx.Playback!.GetFrameMetadata(frames - 1).WallClockTicks - ctx.Playback.GetFrameMetadata(0).WallClockTicks) / (double)TimeSpan.TicksPerSecond : 0d;
                parts[i] = new ReviewParticipant(node, host.NameOf(node), host.RoleKeyOf(node), host.PartLabel(node), ctx.CurrentFdpPath, off, m.IsLocalEntitiesShown(node), ctx.CurrentFrame, frames, seconds);
            }

            var byNode = new ReplicaView?[request.ByNode.Count];
            var watch = new ReplicaView?[request.Watch.Count];
            bool merged = m is not null && facts.IsOpen && facts.Kind == ReviewKind.Merged && _views is not null;
            if (merged)
            {
                for (int i = 0; i < byNode.Length; i++) byNode[i] = request.ByNode[i].NetId > 0 ? _views!.Compute(m!, request.ByNode[i].NetId, request.ByNode[i].Component) : null;
                for (int i = 0; i < watch.Length; i++) watch[i] = request.Watch[i].NetId > 0 ? _views!.ComputeItem(m!, request.Watch[i].NetId, request.Watch[i].Item) : null;
            }

            int primary = m is null ? 0 : (int)m.LocalEntitiesProviderNodeId;
            double simSeconds = 0;
            if (m is not null && m.Contexts.TryGetValue(primary, out var pctx) && pctx.SandboxRepo is { } sandbox && sandbox.HasSingletonUnmanaged<Fdp.Core.GlobalTime>())
                simSeconds = sandbox.GetSingletonUnmanagedRO<Fdp.Core.GlobalTime>().TotalTime;
            var ticks = FrameTicksOf(m, primary);
            return new ReviewReadModel(version, facts, parts, primary, m?.BaseWallTicks ?? 0, nonZero,
                m is null ? 0 : ReplicaViews.StateKey(m), request, byNode, watch, offsets, paths.ToArray(), ticks, simSeconds, _sims, locals);
        }

        private FederatedReplayManager? _ticksManager;
        private int _ticksPrimary = int.MinValue;
        private long[]? _ticks;
        private double[]? _sims;

        /// <summary>The primary's frame → wall-clock table, read once per group / primary (the file's frame index is immutable).</summary>
        private long[]? FrameTicksOf(FederatedReplayManager? m, int primary)
        {
            if (m is null) { _ticksManager = null; _ticks = null; _sims = null; return null; }
            if (ReferenceEquals(m, _ticksManager) && primary == _ticksPrimary) return _ticks;
            _ticksManager = m; _ticksPrimary = primary; _ticks = null; _sims = null;
            if (m.Contexts.TryGetValue(primary, out var ctx) && ctx.Playback is { TotalFrames: > 0 } pb)
            {
                _ticks = new long[pb.TotalFrames];
                _sims = new double[pb.TotalFrames];
                for (int f = 0; f < _ticks.Length; f++)
                {
                    var meta = pb.GetFrameMetadata(f);
                    _ticks[f] = meta.WallClockTicks;
                    _sims[f] = meta.SimTime > 0 || f == 0 ? meta.SimTime : double.NaN;   // as the transport's tooltip reads it: an unstamped frame has no sim time
                }
            }
            return _ticks;
        }

        /// <summary>The version key: every input the model shows, folded into 64 bits. 0 B.</summary>
        public static long VersionOf(FederatedReplayManager? m, in ReviewHostFacts f, ReviewViewRequest request)
        {
            long k = 17;
            k = Mix(k, (int)f.Kind); k = Mix(k, f.IsOpen ? 1 : 0); k = Mix(k, f.IsPending ? 1 : 0); k = Mix(k, f.Failure?.GetHashCode() ?? 0);
            k = Mix(k, f.ExerciseId?.GetHashCode() ?? 0); k = Mix(k, f.PartNode); k = Mix(k, f.CurrentFrame); k = Mix(k, f.TotalFrames);
            k = Mix(k, f.IsPlaying ? 1 : 0); k = Mix(k, RuntimeHelpers.GetHashCode(f.StatusText ?? (object)"")); k = Mix(k, RuntimeHelpers.GetHashCode(f.FaceLabel ?? (object)""));
            k = Mix(k, f.Key); k = Mix(k, request.Version);
            if (m is not null)
            {
                k = Mix(k, RuntimeHelpers.GetHashCode(m)); k = Mix(k, ReplicaViews.StateKey(m)); k = Mix(k, m.BaseWallTicks); k = Mix(k, (int)m.LocalEntitiesProviderNodeId);
                for (int i = 0; i < m.NodeCount; i++)
                {
                    int node = m.NodeIdAt(i);
                    k = Mix(k, node); k = Mix(k, m.NodeOffsets.TryGetValue(node, out long o) ? o : 0L); k = Mix(k, m.IsLocalEntitiesShown(node) ? 1 : 0);
                }
            }
            return k;
        }

        private static long Mix(long k, long v) => unchecked((k ^ v) * 1_099_511_628_211L + (long)0x9E3779B97F4A7C15UL);
    }
}

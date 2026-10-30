using System;
using System.Threading.Tasks;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>The state of one <see cref="BackgroundMeshBuild{TStreams}"/>.</summary>
    public enum MeshBuildState
    {
        /// <summary>Nothing requested yet.</summary>
        Idle,
        /// <summary>The CPU build runs on a worker, or its result waits for the frame thread's upload.</summary>
        Building,
        /// <summary>The result for <see cref="BackgroundMeshBuild{TStreams}.BuiltIdentity"/> is uploaded.</summary>
        Built,
        /// <summary>The build (or its upload) threw: <see cref="BackgroundMeshBuild{TStreams}.Failure"/> says what.</summary>
        Failed,
    }

    /// <summary>
    /// ⭐⭐⭐ <c>R-1046</c> (CE-4078, <c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.9 ①) — <b>the ONE background mesh build</b> both 3-D layers use (<see cref="TerrainLayer3D"/>, the SUMO <c>TrafficLayer3D</c>):
    /// the CPU vertex streams are built on a WORKER keyed by an identity (the geometry / the net), and the FRAME thread takes the finished result and uploads it (<see cref="Pump"/>). Replaces the
    /// identity-check → build → <c>UploadMesh</c>-in-one-<c>Draw</c> that was CE-4078's ~40 s frame stall, in both layers.
    /// </summary>
    /// <remarks>
    /// ⭐ A newer identity SUPERSEDES a pending one: its result is discarded, never uploaded. ⭐ Single-writer: <see cref="Request"/> and <see cref="Pump"/> are called from the frame thread; only the
    /// worker's completion runs elsewhere, and it only fills the ready slot under the lock. ⛔ The build delegate must touch nothing the frame thread mutates (the callers pass immutable terrain / net data).
    /// </remarks>
    public sealed class BackgroundMeshBuild<TStreams> where TStreams : class
    {
        private readonly object _lock = new();
        private long _generation;
        private object? _requested;
        private (object Identity, TStreams Streams)? _ready;

        /// <summary>Where this build stands.</summary>
        public MeshBuildState State { get; private set; }

        /// <summary>The exception message of a failed build or upload; <see langword="null"/> otherwise.</summary>
        public string? Failure { get; private set; }

        /// <summary>The identity whose mesh is uploaded (the last successful <see cref="Pump"/>); <see langword="null"/> before any.</summary>
        public object? BuiltIdentity { get; private set; }

        /// <summary>True when the uploaded mesh is the one for <paramref name="identity"/> — ⭐ the per-load answer: a different identity is NOT built, whatever was built before.</summary>
        public bool IsBuiltFor(object? identity) => State == MeshBuildState.Built && identity != null && Equals(BuiltIdentity, identity);

        /// <summary>Whether a build for <paramref name="identity"/> is running, waiting for upload, built or failed (so a repeated request is a no-op).</summary>
        public bool HasRequested(object identity) { lock (_lock) return State != MeshBuildState.Idle && Equals(_requested, identity); }

        /// <summary>
        /// Starts the CPU build for <paramref name="identity"/> on a worker. The same identity again is a no-op (running, built or failed — a failed build is not retried until the identity changes);
        /// a different one supersedes the pending request (its result is discarded).
        /// </summary>
        public void Request(object identity, Func<TStreams> build)
        {
            if (identity is null) throw new ArgumentNullException(nameof(identity));
            if (build is null) throw new ArgumentNullException(nameof(build));
            long generation;
            lock (_lock)
            {
                if (State != MeshBuildState.Idle && Equals(_requested, identity)) return;
                generation = ++_generation;
                _requested = identity;
                _ready = null;
                State = MeshBuildState.Building;
                Failure = null;
            }
            Task.Run(() =>
            {
                try
                {
                    var streams = build();
                    lock (_lock)
                        if (generation == _generation) _ready = (identity, streams);   // a superseded build's result is dropped here
                }
                catch (Exception ex)
                {
                    lock (_lock)
                        if (generation == _generation) { Failure = ex.Message; State = MeshBuildState.Failed; }
                }
            });
        }

        /// <summary>
        /// FRAME THREAD: takes a finished result (of the CURRENT request) and hands it to <paramref name="upload"/> (the GPU upload). Returns true when it uploaded one.
        /// An upload that throws fails the build with the message.
        /// </summary>
        public bool Pump(Action<TStreams> upload)
        {
            (object Identity, TStreams Streams)? take;
            lock (_lock) { take = _ready; _ready = null; }
            if (take is not { } t) return false;
            try
            {
                upload(t.Streams);
                lock (_lock) { BuiltIdentity = t.Identity; State = MeshBuildState.Built; }
            }
            catch (Exception ex)
            {
                lock (_lock) { Failure = ex.Message; State = MeshBuildState.Failed; }
            }
            return true;
        }

        /// <summary>The source is gone (no terrain, no net): forget everything — nothing is built, nothing is pending (a running worker's result is discarded).</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _generation++;
                _requested = null;
                _ready = null;
                BuiltIdentity = null;
                Failure = null;
                State = MeshBuildState.Idle;
            }
        }
    }
}

using System;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// Per-frame recording system that alternates between keyframes (every
    /// <see cref="KeyframeInterval"/> ticks) and delta frames.
    /// Registered into the scheduler by <see cref="RecordingModule"/> and
    /// <see cref="EpisodeRecorderModule"/> via <see cref="ISystemRegistry"/>.
    /// Runs in the <see cref="SystemPhase.PostSimulation"/> phase so entity state
    /// is fully settled before snapshot capture.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class RecorderTickSystem : IEcsModuleSystem
    {
        /// <summary>Number of delta frames between keyframes.</summary>
        public const int KeyframeInterval = 60;

        private readonly AsyncRecorder _recorder;
        private readonly bool _blocking;
        private int _framesSinceKeyframe;
        private uint _prevTick;
        private long _lastWallTicks;   // the last frame's wall stamp: the recording's timeline never goes back

        // ⭐ CE-3299 B3 (R-3044) — checkpoints asked for and not yet written: (label, the wall time to write at).
        private readonly System.Collections.Generic.List<(string Label, long AtWallTicks)> _requested = new();
        private readonly object _requestedLock = new();

        /// <summary>
        /// ⭐ <c>CE-3299</c> B3 (R-3044, D3) — asks for a checkpoint: a KEYFRAME named <paramref name="label"/> on this node's first
        /// frame whose <c>GlobalTime.TotalWallTicks</c> is at or past <paramref name="atWallTicks"/> (0 = the next frame). Every node
        /// is asked for the same wall time, so the checkpoint lands within one frame on all of them; nothing pauses. A label the
        /// recording already has, or already asked for, is refused.
        /// </summary>
        public void RequestCheckpoint(string label, long atWallTicks)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("a checkpoint needs a label", nameof(label));
            lock (_requestedLock)
            {
                if (_recorder.HasCheckpoint(label) || _requested.Exists(r => r.Label == label))
                    throw new InvalidOperationException($"the recording already has a checkpoint named '{label}'");
                _requested.Add((label, atWallTicks));
            }
        }

        /// <summary>⭐ CE-3299 B3 — checkpoints asked for and not yet written (diagnostics, rails).</summary>
        public int PendingCheckpoints { get { lock (_requestedLock) return _requested.Count; } }

        /// <param name="recorder">The <see cref="AsyncRecorder"/> that owns the output file.</param>
        /// <param name="blocking">
        /// When <c>true</c>, each <c>CaptureFrame</c> / <c>CaptureKeyframe</c> call blocks
        /// until the front-buffer swap completes, preventing delta drops in tight loops.
        /// Mirrors <see cref="RecordingConfiguration.Blocking"/>.
        /// </param>
        /// <param name="checkpointingSeconds">⭐ <c>CE-3299</c> D17 (R-3045) — 0: FULL. N &gt; 0: CHECKPOINTING — a keyframe every N
        /// seconds of sim time (<c>GlobalTime.TotalTime</c>) and at every named checkpoint, and NOTHING on the frames between (no deltas):
        /// restore points, no smooth replay. A paused cluster writes nothing (its sim time does not move).</param>
        /// <param name="slotRunInFlight">⭐ <c>CE-3316</c> — the kernel's "a slot-paced background run is in flight": a due checkpoint waits for a
        /// step where it is not (at most <see cref="MaxCheckpointDeferSteps"/> steps), so a restore continues exactly. Null = never.</param>
        public RecorderTickSystem(AsyncRecorder recorder, bool blocking = false, double checkpointingSeconds = 0, Func<bool>? slotRunInFlight = null)
        {
            _slotRunInFlight = slotRunInFlight;
            _recorder = recorder;
            _blocking = blocking;
            _checkpointingSeconds = Math.Max(0, checkpointingSeconds);
            // Start at KeyframeInterval - 1 so the very first Execute call issues a keyframe.
            _framesSinceKeyframe = KeyframeInterval - 1;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_suspended) return;   // ⭐ CE-3329 P3 (R8) — a review shows the past: nothing of it enters the live history
            if (_resumePending && view is EntityRepository resumed)
            {
                // ⭐ CE-3329 — 📌 measured live (editor, 2026-10-26): the node puts its world back at its commit, but the MASTER moves the
                //   clock back to L a tick later, so the first frames after a review carried the review's clock (3 frames at sim 10.4 s
                //   written into a run paused at 32.1 s). ⇒ resume only once the CLOCK stands at the live point. ⚠ Measured again: the
                //   first frame after the return carries L in the restored singleton and the review's clock after it — so one frame at L
                //   proves nothing; the master's return SNAPS AND PAUSES at L, so the clock stands there frame after frame (bounded).
                if (_resumeAtSimSeconds is { } live && resumed.HasSingletonUnmanaged<GlobalTime>())
                {
                    bool atLive = Math.Abs(resumed.GetSingletonUnmanaged<GlobalTime>().TotalTime - live) <= 1e-6;
                    _framesAtLivePoint = atLive ? _framesAtLivePoint + 1 : 0;
                    if (_framesAtLivePoint < FramesAtLivePointToResume && ++_framesWaitingForLivePoint <= MaxFramesWaitingForLivePoint)
                        return;
                }
                _framesWaitingForLivePoint = 0;
                _framesAtLivePoint = 0;
                // ⭐ CE-3329 F11 / W6 — what the review's swaps destroyed is not a live destruction; the first frame after a review is a
                //   keyframe of the restored world (no delta against a frame of the past), and the keyframe moves the recorder's
                //   destruction mark past them (CE-3335 — ⛔ no longer by emptying the world's log, which other readers share).
                _framesSinceKeyframe = KeyframeInterval - 1;
                _lastKeyframeSimTime = null;
                _resumePending = false;
            }
            // For synchronous (main-thread) modules, view IS the live EntityRepository.
            var repo = (EntityRepository)view;

            // Read the atomic, frame-locked wall-clock timestamp from the GlobalTime singleton.
            // This is the single source of truth populated by the time controller at the start
            // of every frame, guaranteeing all PostSimulation systems see identical ticks.
            // ⭐ backend `2026-10-26` (run C, R-3055) — ONE timeline per recording: the frame-locked clock only, never below the frame
            //   before. 📌 Measured on the editor: the clock resets to 0 during the unload into a replay, and the old fallback stamped
            //   that last frame with the host calendar clock — the replay's span ran from 17 s to the year 2026 (63 928 598 528 s), so its
            //   length and every moment past the first frame were nonsense. ⛔ A wall stamp on a second clock poisons the frame index.
            var globalTime = repo.GetSingletonUnmanaged<GlobalTime>();
            long wallClockTicks = Math.Max(globalTime.TotalWallTicks, _lastWallTicks);
            _lastWallTicks = wallClockTicks;

            bool checkpoint = CheckpointsDue(wallClockTicks, _due);
            // ⭐ CE-3316 (restore design §1l) — not while a slot-paced background run is in flight: its input is an earlier frame's world,
            //   which the keyframe cannot hold, so a restore would run it again a step late. In stepped mode one is in flight for one step
            //   at most (held over paused frames, which do not count).
            if (checkpoint && _slotRunInFlight != null && _deferredSteps < MaxCheckpointDeferSteps && _slotRunInFlight())
            {
                if (deltaTime > 0f) _deferredSteps++;
                checkpoint = false;
            }
            else if (checkpoint) _deferredSteps = 0;
            // ⭐ D17 — CHECKPOINTING: a keyframe on the first frame, every N s of sim time, or at a named checkpoint; nothing else
            bool keyframe = _checkpointingSeconds > 0
                ? checkpoint || _lastKeyframeSimTime is not { } last || globalTime.TotalTime - last >= _checkpointingSeconds || globalTime.TotalTime < last
                : ++_framesSinceKeyframe >= KeyframeInterval || checkpoint;
            if (_checkpointingSeconds > 0 && !keyframe) return;
            if (keyframe)
            {
                StampWorld(repo, _recorder.Metadata);   // CE-3126 — cheap; once per keyframe keeps it current
                _recorder.CaptureKeyframe(repo, wallClockTicks, blocking: _blocking, eventBus: repo.Bus);
                _framesSinceKeyframe = 0;   // ⭐ CE-3299 B3 — a checkpoint IS this interval's keyframe: the next periodic one moves on
                _lastKeyframeSimTime = globalTime.TotalTime;   // D17 — the same in checkpointing mode
                if (checkpoint)   // ⭐ CE-3316 — a periodic keyframe while a checkpoint waits is not that checkpoint
                {
                    foreach (var label in _due) _recorder.MarkCheckpoint(label, wallClockTicks, globalTime);
                    _due.Clear();
                }
                // ⭐ CE-3313 — every recording's manifest goes to disk with its first frame (a branch's carries its parent and inherited
                //   checkpoints — CE-3299 B4 / D18), so a node killed mid-session leaves a recording that names itself; finalize rewrites it
                if (!_wroteManifest)
                {
                    _wroteManifest = true;
                    _recorder.WriteMetadata();
                }
            }
            else
            {
                _recorder.CaptureFrame(repo, _prevTick, wallClockTicks, blocking: _blocking, eventBus: repo.Bus);
            }

            _prevTick = repo.GlobalVersion;
        }

        private volatile bool _suspended;
        private bool _resumePending;

        /// <summary>
        /// ⭐ CE-3329 P3 (R8, row 17) — a review is open: write NOTHING (the world is the replayed one) and keep the file open, so the same
        /// recording resumes after the review. ⚠ Call <see cref="RecordingModule.WaitForWrites"/> after it to flush the last live frame.
        /// </summary>
        public void Suspend() => _suspended = true;

        /// <summary>⭐ CE-3329 P3 (F11, W6) — the review is over: record on in the same file; the first frame recorded clears the destruction log
        /// and is a keyframe. <paramref name="atSimSeconds"/>: wait until the world's clock is back there (the live point L) — the master
        /// moves the clock after the node has put its world back.</summary>
        public void Resume(double? atSimSeconds = null)
        {
            _resumeAtSimSeconds = atSimSeconds;
            _resumePending = true;
            _suspended = false;
        }

        private double? _resumeAtSimSeconds;
        private int _framesWaitingForLivePoint;
        private int _framesAtLivePoint;

        /// <summary>⭐ CE-3329 — consecutive frames the clock must stand at the live point before a resumed recording writes again.</summary>
        public const int FramesAtLivePointToResume = 3;

        /// <summary>⭐ CE-3329 — how many frames a resume waits for the clock to reach the live point before it records anyway.</summary>
        public const int MaxFramesWaitingForLivePoint = 600;

        /// <summary>⭐ CE-3329 — true while a review holds the recording.</summary>
        public bool IsSuspended => _suspended;

        private readonly System.Collections.Generic.List<string> _due = new();
        private readonly Func<bool>? _slotRunInFlight;   // CE-3316
        private int _deferredSteps;

        /// <summary>⭐ <c>CE-3316</c> — how many steps a due checkpoint waits for a frame with no slot run in flight, at most.</summary>
        public const int MaxCheckpointDeferSteps = 8;
        private bool _wroteManifest;
        private readonly double _checkpointingSeconds;   // D17 — 0 = FULL
        private double? _lastKeyframeSimTime;

        /// <summary>⭐ <c>CE-3299</c> D17 — the recording mode: 0 = FULL, N &gt; 0 = a keyframe every N s of sim time only.</summary>
        public double CheckpointingSeconds => _checkpointingSeconds;

        // ⭐ CE-3299 B3 — moves every request whose wall time has come into due; true when there is one.
        private bool CheckpointsDue(long wallClockTicks, System.Collections.Generic.List<string> due)
        {
            lock (_requestedLock)
            {
                if (_requested.Count == 0) return due.Count > 0;   // ⭐ CE-3316 — a checkpoint still waiting for a quiet step
                for (int i = _requested.Count - 1; i >= 0; i--)
                {
                    if (_requested[i].AtWallTicks > wallClockTicks) continue;
                    due.Insert(0, _requested[i].Label);
                    _requested.RemoveAt(i);
                }
            }
            return due.Count > 0;
        }

        /// <summary>
        /// ⭐ <c>CE-3126</c> (R-229) — writes the recorded world's terrain name and geo origin into <paramref name="metadata"/>:
        /// the terrain so a replay can load it by name, the origin so it is placed where it was recorded even if that terrain
        /// is gone or its origin edited since. Both read from world singletons; a world without them leaves the fields as
        /// they are. 📄 docs/DESIGN_Geo_Origin.md §2 E.
        /// </summary>
        public static void StampWorld(EntityRepository repo, Fdp.Core.FlightRecorder.Metadata.RecordingMetadata metadata)
        {
            metadata.TerrainName = Fdp.Toolkit.Terrain.TerrainDefinition.ResidentName(repo) ?? "";
            var (scenario, hash) = Fdp.Toolkit.Scenario.ScenarioDescription.IdentityOf(repo);   // ⭐ CE-4058 ② — the scenario it ran
            metadata.ScenarioName = scenario ?? "";
            metadata.ScenarioHash = hash ?? "";
            if (repo.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                && repo.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>() is { } geo)
            {
                var (lat, lon, alt) = geo.Origin;
                var o = metadata.GeoOrigin ??= new Fdp.Core.FlightRecorder.Metadata.GeoOriginRecord();
                o.Lat = lat; o.Lon = lon; o.Alt = alt;
            }
        }
    }
}

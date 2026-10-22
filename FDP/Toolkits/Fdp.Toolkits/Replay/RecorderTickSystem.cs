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
        public RecorderTickSystem(AsyncRecorder recorder, bool blocking = false)
        {
            _recorder = recorder;
            _blocking = blocking;
            // Start at KeyframeInterval - 1 so the very first Execute call issues a keyframe.
            _framesSinceKeyframe = KeyframeInterval - 1;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // For synchronous (main-thread) modules, view IS the live EntityRepository.
            var repo = (EntityRepository)view;

            // Read the atomic, frame-locked wall-clock timestamp from the GlobalTime singleton.
            // This is the single source of truth populated by the time controller at the start
            // of every frame, guaranteeing all PostSimulation systems see identical ticks.
            // Falls back to DateTime.UtcNow.Ticks until Phase 3 time controllers populate
            // GlobalTime.TotalWallTicks (WCR-P3-T002/T003).
            var globalTime = repo.GetSingletonUnmanaged<GlobalTime>();
            long wallClockTicks = globalTime.TotalWallTicks != 0
                ? globalTime.TotalWallTicks
                : DateTime.UtcNow.Ticks;

            bool checkpoint = CheckpointsDue(wallClockTicks, _due);
            if (++_framesSinceKeyframe >= KeyframeInterval || checkpoint)
            {
                StampWorld(repo, _recorder.Metadata);   // CE-3126 — cheap; once per keyframe keeps it current
                _recorder.CaptureKeyframe(repo, wallClockTicks, blocking: _blocking, eventBus: repo.Bus);
                _framesSinceKeyframe = 0;   // ⭐ CE-3299 B3 — a checkpoint IS this interval's keyframe: the next periodic one moves on
                foreach (var label in _due) _recorder.MarkCheckpoint(label, wallClockTicks, globalTime);
                _due.Clear();
                // ⭐ CE-3299 B4 / D18 — a branch's manifest (its parent, its inherited checkpoints) goes to disk with its first frame
                if (!_wroteBranchManifest && _recorder.Metadata.ParentExerciseId != Guid.Empty)
                {
                    _wroteBranchManifest = true;
                    _recorder.WriteMetadata();
                }
            }
            else
            {
                _recorder.CaptureFrame(repo, _prevTick, wallClockTicks, blocking: _blocking, eventBus: repo.Bus);
            }

            _prevTick = repo.GlobalVersion;
        }

        private readonly System.Collections.Generic.List<string> _due = new();
        private bool _wroteBranchManifest;

        // ⭐ CE-3299 B3 — moves every request whose wall time has come into due; true when there is one.
        private bool CheckpointsDue(long wallClockTicks, System.Collections.Generic.List<string> due)
        {
            lock (_requestedLock)
            {
                if (_requested.Count == 0) return false;
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

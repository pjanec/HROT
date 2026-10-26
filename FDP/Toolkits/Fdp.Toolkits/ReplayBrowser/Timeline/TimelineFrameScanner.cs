using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-3330</c> (R-4087 addendum 6; docs/DESIGN_Director_Timeline_Editor.md §2c rule 7, §2d) — the index's PER-FRAME STEP, one class:
    /// every registered lane compiled once (<see cref="TimelineLaneCompiler"/>), evaluated on a world for one frame, its matches appended as
    /// <see cref="TimelineEvent"/>s with the frame's ticks. ⭐ The recording's builder (<see cref="TimelineIndexBuilder"/>) and the live source
    /// (<see cref="LiveTimelineSource"/>) both run THIS — so a live index and the index rebuilt from its recording hold the same events, lane
    /// by lane; ⛔ never a second evaluator.
    /// </summary>
    public sealed class TimelineFrameScanner
    {
        private readonly List<(string Lane, ILaneScan Scan)> _scans = new();
        private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TimelineEvent>> _sink = new(StringComparer.Ordinal);
        private readonly List<SearchResultDto> _results = new(64);
        private readonly List<long> _ticks = new();

        public TimelineFrameScanner(IReadOnlyList<IndexedLaneDefinition> lanes, TimelineLaneCompiler compiler)
        {
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            if (compiler == null) throw new ArgumentNullException(nameof(compiler));
            foreach (var lane in lanes)
            {
                _sink[lane.Lane.Id] = new List<TimelineEvent>();
                try { _scans.Add((lane.Lane.Id, compiler.Compile(lane))); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { _errors[lane.Lane.Id] = ex.Message; }
            }
        }

        /// <summary>Frames stepped so far (the next frame's index).</summary>
        public int Frames => _ticks.Count;

        /// <summary>
        /// Evaluates every lane on <paramref name="repo"/> as frame <paramref name="frame"/> at <paramref name="ticks"/>. ⚠ The structural lanes
        /// read the world's destruction log: a PLAYBACK world's caller clears it after each frame (<paramref name="clearDestructionLog"/>);
        /// ⛔ a LIVE world's must not — its log belongs to the recorder, and a structural scan reports a death once whatever the log holds.
        /// </summary>
        public void Step(EntityRepository repo, int frame, long ticks, bool clearDestructionLog)
        {
            while (_ticks.Count <= frame) _ticks.Add(ticks);
            _ticks[frame] = ticks;
            foreach (var (laneId, scan) in _scans)
            {
                _results.Clear();
                scan.Scan(repo, frame, ticks, _results);
                var into = _sink[laneId];
                foreach (var r in _results)
                {
                    var p = r.Payload;
                    into.Add(p is { } payload
                        ? new TimelineEvent(frame, laneId, payload.Subject, payload.Other, payload.X, payload.Y, payload.Value)
                        : new TimelineEvent(frame, laneId, ResultIds.Of(repo, r.Entity), 0, 0, 0, 0));
                }
            }
            if (clearDestructionLog) repo.ClearDestructionLog();   // one frame's deaths belong to that frame
        }

        /// <summary>An immutable index of the frames stepped so far, out of <paramref name="totalFrames"/> (its frame table padded to it with
        /// the last ticks when the caller pre-sized it).</summary>
        public TimelineIndex Snapshot(int scannedFrames, int totalFrames, long[]? frameTicks = null)
        {
            var lanes = new Dictionary<string, TimelineEvent[]>(_sink.Count, StringComparer.Ordinal);
            foreach (var (id, list) in _sink) lanes[id] = list.ToArray();
            var ticks = frameTicks ?? _ticks.GetRange(0, Math.Min(scannedFrames, _ticks.Count)).ToArray();
            return new TimelineIndex(lanes, ticks, scannedFrames, totalFrames, new Dictionary<string, string>(_errors));
        }

        /// <summary>The frame → ticks table so far (a copy).</summary>
        public long[] FrameTicks() => _ticks.ToArray();
    }
}

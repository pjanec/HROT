using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (§2b "events": frame, lane, subject id, other id, x, y, one value) — one indexed event. <see cref="Lane"/> is the lane's
    /// registered id (R-4079 — no lane enum). Ids are NETWORK ids where the entity had one, else <c>-(index + 1)</c>, so they mean the same
    /// thing in the index, the sidecar and the live world.
    /// </summary>
    public readonly record struct TimelineEvent(int Frame, string Lane, long Subject, long Other, float X, float Y, float Value);

    /// <summary>
    /// ⭐ <c>CE-1302</c> (§9 TL-4, §9c) — an IMMUTABLE snapshot of a recording's event index: per lane id, its events in frame order; one frame →
    /// wall-clock ticks table; the frames scanned so far; and, per lane that could not be built, why. The builder publishes a new snapshot
    /// every chunk, so a reader on the frame thread never sees a half-written list.
    /// </summary>
    public sealed class TimelineIndex
    {
        private readonly Dictionary<string, TimelineEvent[]> _lanes;

        public TimelineIndex(IReadOnlyDictionary<string, TimelineEvent[]> lanes, long[] frameTicks, int scannedFrames, int totalFrames,
            IReadOnlyDictionary<string, string>? laneErrors = null)
        {
            _lanes = new Dictionary<string, TimelineEvent[]>(lanes, StringComparer.Ordinal);
            FrameTicks = frameTicks;
            ScannedFrames = scannedFrames;
            TotalFrames = totalFrames;
            LaneErrors = laneErrors ?? new Dictionary<string, string>();
        }

        public static readonly TimelineIndex Empty = new(new Dictionary<string, TimelineEvent[]>(), Array.Empty<long>(), 0, 0);

        /// <summary>Wall-clock ticks of each scanned frame (index = frame).</summary>
        public IReadOnlyList<long> FrameTicks { get; }
        /// <summary>Frames scanned so far (== <see cref="TotalFrames"/> when complete).</summary>
        public int ScannedFrames { get; }
        public int TotalFrames { get; }
        public bool IsComplete => ScannedFrames >= TotalFrames;
        /// <summary>The lanes this index holds.</summary>
        public IReadOnlyCollection<string> LaneIds => _lanes.Keys;
        /// <summary>Per lane whose definition could not be evaluated, the reason (the lane is then empty).</summary>
        public IReadOnlyDictionary<string, string> LaneErrors { get; }

        public bool Has(string lane) => _lanes.ContainsKey(lane);

        /// <summary>The lane's events, in frame order (empty for a lane the index does not hold).</summary>
        public IReadOnlyList<TimelineEvent> Lane(string lane) => Raw(lane);

        /// <summary>How many of the lane's events fall in frames [<paramref name="from"/>, <paramref name="to"/>).</summary>
        public int Count(string lane, int from, int to)
        {
            var a = Raw(lane);
            return LowerBound(a, to) - LowerBound(a, from);
        }

        /// <summary>How many of the lane's events fall before <paramref name="frame"/> — a position, so a run of edges costs one search each.</summary>
        public int Before(string lane, int frame) => LowerBound(Raw(lane), frame);

        /// <summary>The lane's events in frames [<paramref name="from"/>, <paramref name="to"/>).</summary>
        public ReadOnlySpan<TimelineEvent> Events(string lane, int from, int to)
        {
            var a = Raw(lane);
            int lo = LowerBound(a, from), hi = LowerBound(a, to);
            return new ReadOnlySpan<TimelineEvent>(a, lo, Math.Max(0, hi - lo));
        }

        /// <summary>The frame whose wall-clock ticks are nearest at or before <paramref name="ticks"/> (0 when none).</summary>
        public int FrameAt(long ticks)
        {
            int lo = 0, hi = FrameTicks.Count - 1, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (FrameTicks[mid] <= ticks) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best;
        }

        internal TimelineEvent[] Raw(string lane) => _lanes.TryGetValue(lane, out var a) ? a : Array.Empty<TimelineEvent>();

        private static int LowerBound(TimelineEvent[] a, int frame)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid].Frame < frame) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }
}

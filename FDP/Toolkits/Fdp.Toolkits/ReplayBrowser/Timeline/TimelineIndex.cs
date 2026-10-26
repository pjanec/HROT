using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9 TL-5) — the lanes R1 indexes. ⏳ Movement and Tasks wait (R2b): they need
    /// the scenario's areas and mission plans.
    /// </summary>
    public enum TimelineLane : byte
    {
        /// <summary>Life: a network id appeared.</summary>
        Spawned = 0,
        /// <summary>Life: a network id left the world.</summary>
        Destroyed = 1,
        /// <summary>Combat: a round fired (<c>WeaponFireNotification</c>, not refused).</summary>
        Shot = 2,
        /// <summary>Combat: a round hit an entity (<c>HitEvent</c>).</summary>
        Hit = 3,
        /// <summary>Combat: a detonation (<c>DetonationNotification</c>).</summary>
        Blast = 4,
        /// <summary>Behaviour (TE-22): a brain journal entry (<c>BrainJournal.Seq</c> advanced).</summary>
        Change = 5,
        /// <summary>Behaviour: a commander's order history gained a new entry.</summary>
        Order = 6,
        /// <summary>Behaviour: a commander's report history gained a new entry.</summary>
        Report = 7,
        /// <summary>Behaviour: a utility decision switched its winner.</summary>
        Decision = 8,
    }

    /// <summary>⭐ <c>CE-1302</c> — the lane groups §2b collapses (a collapsed group is one density strip).</summary>
    public enum TimelineLaneGroup : byte { Life = 0, Combat = 1, Behaviour = 2 }

    public static class TimelineLanes
    {
        public const int Count = 9;

        public static TimelineLaneGroup GroupOf(TimelineLane lane) => lane switch
        {
            TimelineLane.Spawned or TimelineLane.Destroyed => TimelineLaneGroup.Life,
            TimelineLane.Shot or TimelineLane.Hit or TimelineLane.Blast => TimelineLaneGroup.Combat,
            _ => TimelineLaneGroup.Behaviour,
        };

        /// <summary>§2b "priority on top": death &gt; detonation &gt; hit &gt; shot &gt; behaviour &gt; spawn — a cluster shows its highest.</summary>
        public static int PriorityOf(TimelineLane lane) => lane switch
        {
            TimelineLane.Destroyed => 6,
            TimelineLane.Blast => 5,
            TimelineLane.Hit => 4,
            TimelineLane.Shot => 3,
            TimelineLane.Order or TimelineLane.Report => 2,
            TimelineLane.Change or TimelineLane.Decision => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-1302</c> (§2b "events": frame, lane, subject id, other id, x, y, one value) — one indexed event. Ids are NETWORK ids where the
    /// entity had one, else <c>-(index + 1)</c>, so they mean the same thing in the index, the sidecar and the live world.
    /// </summary>
    public readonly record struct TimelineEvent(int Frame, TimelineLane Lane, long Subject, long Other, float X, float Y, float Value);

    /// <summary>
    /// ⭐ <c>CE-1302</c> (§9 TL-4) — an IMMUTABLE snapshot of a recording's event index: per lane, its events in frame order; one frame →
    /// wall-clock ticks table; the frames scanned so far. The builder publishes a new snapshot every chunk, so a reader on the frame thread
    /// never sees a half-written list.
    /// </summary>
    public sealed class TimelineIndex
    {
        private readonly TimelineEvent[][] _lanes;

        public TimelineIndex(TimelineEvent[][] lanes, long[] frameTicks, int scannedFrames, int totalFrames)
        {
            if (lanes.Length != TimelineLanes.Count) throw new ArgumentException("one array per lane", nameof(lanes));
            _lanes = lanes;
            FrameTicks = frameTicks;
            ScannedFrames = scannedFrames;
            TotalFrames = totalFrames;
        }

        public static readonly TimelineIndex Empty = new(NewLanes(), Array.Empty<long>(), 0, 0);

        /// <summary>Wall-clock ticks of each scanned frame (index = frame).</summary>
        public IReadOnlyList<long> FrameTicks { get; }
        /// <summary>Frames scanned so far (== <see cref="TotalFrames"/> when complete).</summary>
        public int ScannedFrames { get; }
        public int TotalFrames { get; }
        public bool IsComplete => ScannedFrames >= TotalFrames;

        /// <summary>The lane's events, in frame order.</summary>
        public IReadOnlyList<TimelineEvent> Lane(TimelineLane lane) => _lanes[(int)lane];

        /// <summary>How many of the lane's events fall in frames [<paramref name="from"/>, <paramref name="to"/>).</summary>
        public int Count(TimelineLane lane, int from, int to)
        {
            var a = _lanes[(int)lane];
            return LowerBound(a, to) - LowerBound(a, from);
        }

        /// <summary>How many of the lane's events fall before <paramref name="frame"/> — a position, so a run of edges costs one search each.</summary>
        public int Before(TimelineLane lane, int frame) => LowerBound(_lanes[(int)lane], frame);

        /// <summary>The lane's events in frames [<paramref name="from"/>, <paramref name="to"/>).</summary>
        public ReadOnlySpan<TimelineEvent> Events(TimelineLane lane, int from, int to)
        {
            var a = _lanes[(int)lane];
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

        internal static TimelineEvent[][] NewLanes()
        {
            var lanes = new TimelineEvent[TimelineLanes.Count][];
            for (int i = 0; i < lanes.Length; i++) lanes[i] = Array.Empty<TimelineEvent>();
            return lanes;
        }

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

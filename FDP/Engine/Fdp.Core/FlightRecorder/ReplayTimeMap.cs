using System;
using System.Collections.Generic;
using System.Linq;
namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-3329</c> P1 (row 7) — a recording's two clocks side by side: wall ↔ sim, with the operator's paused stretches.</b>
    /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §0, §4, §5 R1–R4.
    /// </summary>
    /// <remarks>
    /// <para>⚠ In <c>Fdp.Core</c> beside the frame index it is built from, not in <c>Fdp.Toolkits/Replay</c> as §3b drew it: it crosses
    /// the <c>IRecordReplayController</c> seam (a node's prepare result carries its map, R3), which lives in <c>Fdp.Core</c>.</para>
    /// <para>⭐ ONE replay clock (wall), TWO views of it: the SIM axis is the wall clock with every flat run of sim time (a pause, a gap
    /// between steps) collapsed to an instant. Built from the v7 frame index (<see cref="FrameMetadata.SimTime"/>,
    /// <see cref="FrameFlags"/>) — no frame is decoded.</para>
    /// <para>⭐ Stored as piecewise-linear KEYPOINTS (wall, sim): sim(wall) is linear between two keypoints and constant across a flat run
    /// (two keypoints with one sim). Consecutive frames on one line (within <see cref="SimToleranceSeconds"/>) share a segment, so an hour
    /// at 60 Hz with a handful of pauses is a few dozen keypoints — small enough for every node's prepare result (R3).</para>
    /// <para>⭐ <see cref="Stretches"/> are the OPERATOR's pauses only (R-3063 D-H): a flat run that follows a short advancing run
    /// (≤ <see cref="StepFrames"/> frames — one lockstep step) which itself followed a flat run is a gap between steps and collapses
    /// with no mark; the pause BEFORE the first step is the operator's. ⚠ As built this is read
    /// from the frame pattern, not from a recorded halt reason: on a node, an operator pause and a gap between steps are the same clock
    /// state (deterministic, nothing in flight) — design §12f-P0.</para>
    /// </remarks>
    public sealed class ReplayTimeMap
    {
        /// <summary>The largest sim error a keypoint segment may hide (seconds).</summary>
        public const double SimToleranceSeconds = 0.0005;

        /// <summary>An advancing run this short (frames) between two flat runs is one step of a stepped run (D-H).</summary>
        public const int StepFrames = 2;

        /// <summary>A keypoint of the piecewise-linear sim(wall) curve.</summary>
        public readonly record struct Point(long WallTicks, double SimSeconds);

        /// <summary>
        /// An OPERATOR's paused stretch: the wall span the clock stood still at <see cref="SimSeconds"/>, from its first halted frame to
        /// the frame that ran again (the unpause instant — where its changes show on the sim axis). <see cref="Changed"/>: some frame in
        /// it carried a change besides the clock (the timeline's filled mark).
        /// </summary>
        public readonly record struct Stretch(long StartWallTicks, long EndWallTicks, double SimSeconds, bool Changed);

        /// <summary>How a sim time that is a flat instant resolves to wall time.</summary>
        public enum Edge
        {
            /// <summary>The flat run's FIRST frame — before what happened during it.</summary>
            BeforeStretch,

            /// <summary>The flat run's END — its changes shown (a sim seek's default, design §4).</summary>
            AfterStretch,
        }

        public IReadOnlyList<Point> Points { get; }
        public IReadOnlyList<Stretch> Stretches { get; }

        /// <summary>False for a recording whose index carries no sim time (format v6) — every query then answers on wall alone.</summary>
        public bool HasSimTime => Points.Count > 0;

        public ReplayTimeMap(IReadOnlyList<Point> points, IReadOnlyList<Stretch> stretches)
        {
            Points = points ?? throw new ArgumentNullException(nameof(points));
            Stretches = stretches ?? throw new ArgumentNullException(nameof(stretches));
        }

        public static readonly ReplayTimeMap Empty = new(Array.Empty<Point>(), Array.Empty<Stretch>());

        /// <summary>From an open recording's frame index. A v6 recording (no sim time) gives <see cref="Empty"/>.</summary>
        public static ReplayTimeMap FromRecording(PlaybackController playback)
        {
            if (playback is null) throw new ArgumentNullException(nameof(playback));
            int n = playback.TotalFrames;
            var wall = new long[n];
            var sim = new double[n];
            var flags = new FrameFlags[n];
            for (int i = 0; i < n; i++)
            {
                var m = playback.GetFrameMetadata(i);
                wall[i] = m.WallClockTicks;
                sim[i] = m.SimTime;
                flags[i] = m.Flags;
            }
            return FromFrames(wall, sim, flags);
        }

        /// <summary>From the frame index's three columns (one entry per frame, in file order).</summary>
        public static ReplayTimeMap FromFrames(ReadOnlySpan<long> wall, ReadOnlySpan<double> sim, ReadOnlySpan<FrameFlags> flags)
        {
            int n = wall.Length;
            if (sim.Length != n || flags.Length != n) throw new ArgumentException("one wall, sim and flags entry per frame");
            if (n == 0) return Empty;
            for (int i = 0; i < n; i++)
                if ((flags[i] & FrameFlags.Unknown) != 0 || double.IsNaN(sim[i])) return Empty;   // v6: no sim axis

            // ── keypoints: greedy piecewise-linear over (wall, sim) — O(n): every frame since the segment's anchor narrows the window of
            //   slopes that keep it within the tolerance; a frame whose slope falls outside closes the segment. A switch between a flat
            //   run and a slope always closes it, so a flat run is kept exact.
            var points = new List<Point> { new(wall[0], sim[0]) };
            int anchor = 0;
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            for (int i = 1; i < n; i++)
            {
                bool flat = sim[i] == sim[i - 1];
                bool wasFlat = i >= 2 && sim[i - 1] == sim[i - 2];
                double dw = wall[i] - wall[anchor];
                double slope = dw > 0 ? (sim[i] - sim[anchor]) / dw : 0;
                bool breaks = (flat != wasFlat && i - 1 > anchor) || (dw > 0 && (slope < lo || slope > hi)) || (dw <= 0 && sim[i] != sim[anchor]);
                if (breaks)
                {
                    anchor = i - 1;
                    points.Add(new(wall[anchor], sim[anchor]));
                    lo = double.NegativeInfinity; hi = double.PositiveInfinity;
                    dw = wall[i] - wall[anchor];
                }
                if (dw > 0)
                {
                    lo = Math.Max(lo, (sim[i] - SimToleranceSeconds - sim[anchor]) / dw);
                    hi = Math.Min(hi, (sim[i] + SimToleranceSeconds - sim[anchor]) / dw);
                }
            }
            if (points[^1].WallTicks != wall[n - 1]) points.Add(new(wall[n - 1], sim[n - 1]));

            // ── flat runs → the operator's stretches (D-H): not a gap between two short steps ──
            var runs = new List<(int First, int Last)>();   // frames whose sim equals the previous frame's (the clock stood still)
            for (int i = 1; i < n; i++)
            {
                if (sim[i] != sim[i - 1]) continue;
                int first = i;
                while (i + 1 < n && sim[i + 1] == sim[i]) i++;
                runs.Add((first, i));
            }
            var stretches = new List<Stretch>();
            for (int r = 0; r < runs.Count; r++)
            {
                var (first, last) = runs[r];
                int advBefore = first - (r == 0 ? 0 : runs[r - 1].Last + 1);   // advancing frames since the previous run
                bool stepGap = r > 0 && advBefore <= StepFrames;   // it follows one step, which followed a flat run
                if (stepGap) continue;
                bool changed = false;
                for (int i = first; i <= last; i++) changed |= (flags[i] & FrameFlags.Changed) != 0;
                long end = last + 1 < n ? wall[last + 1] : wall[last];
                stretches.Add(new Stretch(wall[first - 1], end, sim[first], changed));
            }
            return new ReplayTimeMap(points, stretches);
        }

        /// <summary>The recorded sim time at <paramref name="wallTicks"/> (clamped to the recording). NaN without a sim axis.</summary>
        public double SimAt(long wallTicks)
        {
            if (!HasSimTime) return double.NaN;
            if (wallTicks <= Points[0].WallTicks) return Points[0].SimSeconds;
            if (wallTicks >= Points[^1].WallTicks) return Points[^1].SimSeconds;
            int hi = UpperPoint(wallTicks);
            var a = Points[hi - 1];
            var b = Points[hi];
            if (b.WallTicks == a.WallTicks) return b.SimSeconds;
            return a.SimSeconds + (b.SimSeconds - a.SimSeconds) * (wallTicks - a.WallTicks) / (double)(b.WallTicks - a.WallTicks);
        }

        /// <summary>
        /// ⭐ <c>CE-3329</c> row 9 — the recorded RATE at <paramref name="wallTicks"/>: sim seconds per wall second of the segment it lies
        /// in (2 for a run recorded at 2×, 0 inside a flat run). 1 without a sim axis or outside the recording (neutral).
        /// </summary>
        public double RateAt(long wallTicks)
        {
            if (!HasSimTime || wallTicks < Points[0].WallTicks || wallTicks >= Points[^1].WallTicks) return 1.0;
            int hi = UpperPoint(wallTicks);
            var a = Points[hi - 1];
            var b = Points[hi];
            if (b.WallTicks == a.WallTicks) return 1.0;
            return (b.SimSeconds - a.SimSeconds) / ((b.WallTicks - a.WallTicks) / (double)TimeSpan.TicksPerSecond);
        }

        /// <summary>
        /// The wall time of sim time <paramref name="simSeconds"/> (clamped). A sim time that is a flat instant (a pause) resolves to its
        /// first frame or — <see cref="Edge.AfterStretch"/>, the default — its end, so what changed during it is shown.
        /// </summary>
        public long WallAt(double simSeconds, Edge edge = Edge.AfterStretch)
        {
            if (!HasSimTime) throw new InvalidOperationException("this recording has no sim axis (format v6)");
            if (simSeconds <= Points[0].SimSeconds + SimToleranceSeconds)
                return edge == Edge.AfterStretch ? LastWallWithSim(Points[0].SimSeconds) : Points[0].WallTicks;
            if (simSeconds >= Points[^1].SimSeconds - SimToleranceSeconds)
                return edge == Edge.AfterStretch ? Points[^1].WallTicks : FirstWallWithSim(Points[^1].SimSeconds);
            for (int i = 1; i < Points.Count; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                if (simSeconds > b.SimSeconds + SimToleranceSeconds) continue;
                if (Same(simSeconds, b.SimSeconds))
                    return edge == Edge.AfterStretch ? LastWallWithSim(b.SimSeconds) : FirstWallWithSim(b.SimSeconds);
                if (b.SimSeconds == a.SimSeconds) return a.WallTicks;
                return a.WallTicks + (long)Math.Round((b.WallTicks - a.WallTicks) * (simSeconds - a.SimSeconds) / (b.SimSeconds - a.SimSeconds));
            }
            return Points[^1].WallTicks;
        }

        /// <summary>When <paramref name="wallTicks"/> stands inside a flat run (any — an operator pause or a gap between steps), the wall
        /// time it ends (the sim axis jumps there); otherwise <see langword="null"/>.</summary>
        public long? NextUnpause(long wallTicks)
        {
            if (!HasSimTime) return null;
            for (int i = 1; i < Points.Count; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                if (b.SimSeconds != a.SimSeconds || b.WallTicks == a.WallTicks) continue;
                if (wallTicks >= a.WallTicks && wallTicks < b.WallTicks) return LastWallWithSim(a.SimSeconds);
            }
            return null;
        }

        /// <summary>
        /// ⭐ R3 — the CLUSTER's map: the first map's sim times (the anchor node's), and a stretch only where EVERY map paused (the
        /// intersection of their wall spans; changed if any node saw a change). Maps without a sim axis are ignored.
        /// </summary>
        public static ReplayTimeMap Intersect(IEnumerable<ReplayTimeMap> maps)
        {
            var withSim = maps.Where(m => m != null && m.HasSimTime).ToList();
            if (withSim.Count == 0) return Empty;
            var stretches = withSim[0].Stretches.ToList();
            foreach (var other in withSim.Skip(1))
            {
                var next = new List<Stretch>();
                foreach (var s in stretches)
                    foreach (var o in other.Stretches)
                    {
                        long start = Math.Max(s.StartWallTicks, o.StartWallTicks);
                        long end = Math.Min(s.EndWallTicks, o.EndWallTicks);
                        if (end > start) next.Add(new Stretch(start, end, s.SimSeconds, s.Changed || o.Changed));
                    }
                stretches = next;
            }
            return new ReplayTimeMap(withSim[0].Points, stretches);
        }

        // sim times summed frame by frame are not exact (10 × 0.1 ≠ 1.0): a sim query matches within the map's tolerance
        private static bool Same(double a, double b) => Math.Abs(a - b) <= SimToleranceSeconds;

        private int UpperPoint(long wallTicks)
        {
            int lo = 1, hi = Points.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Points[mid].WallTicks < wallTicks) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private long LastWallWithSim(double simSeconds)
        {
            long wall = Points[0].WallTicks;
            foreach (var p in Points)
            {
                if (p.SimSeconds > simSeconds + SimToleranceSeconds) break;
                if (Same(p.SimSeconds, simSeconds)) wall = p.WallTicks;
            }
            return wall;
        }

        private long FirstWallWithSim(double simSeconds)
        {
            foreach (var p in Points)
                if (p.SimSeconds >= simSeconds - SimToleranceSeconds) return p.WallTicks;
            return Points[^1].WallTicks;
        }
    }
}

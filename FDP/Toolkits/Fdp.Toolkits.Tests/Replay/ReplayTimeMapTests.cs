using System;
using System.Linq;
using Fdp.Core.FlightRecorder;
using Xunit;

namespace Fdp.Toolkits.Tests.Replay
{
    /// <summary>
    /// ⭐⭐ <c>CE-3329</c> P1 (row 7) — <see cref="ReplayTimeMap"/>: the recording's wall ↔ sim map from the v7 frame index, the operator's
    /// paused stretches (D-H: a gap between steps is not one), sim seeks that land after a stretch, and the cluster's intersection (R3).
    /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §4, §5.
    /// </summary>
    public sealed class ReplayTimeMapTests
    {
        private const long T = TimeSpan.TicksPerSecond / 10;   // one frame = 0.1 s of wall

        /// <summary>Frames at 0.1 s of wall each; <paramref name="dSim"/>[i] = sim advance of frame i (0 = halted).</summary>
        private static ReplayTimeMap Map(double[] dSim, Func<int, bool>? changed = null)
        {
            int n = dSim.Length;
            var wall = new long[n];
            var sim = new double[n];
            var flags = new FrameFlags[n];
            double s = 0;
            for (int i = 0; i < n; i++)
            {
                s += dSim[i];
                wall[i] = 1_000_000 + i * T;
                sim[i] = s;
                flags[i] = (dSim[i] == 0 ? FrameFlags.Halted : FrameFlags.None) | (changed?.Invoke(i) == true ? FrameFlags.Changed : FrameFlags.None);
            }
            return ReplayTimeMap.FromFrames(wall, sim, flags);
        }

        private static double[] Run(int frames, double d = 0.1) => Enumerable.Repeat(d, frames).ToArray();
        private static double[] Pause(int frames) => new double[frames];

        [Fact]
        public void A_straight_run_is_one_line_and_maps_both_ways()
        {
            var map = Map(Run(100));
            Assert.Equal(2, map.Points.Count);
            Assert.Empty(map.Stretches);
            Assert.Equal(5.0, map.SimAt(1_000_000 + 49 * T), 6);
            Assert.Equal(1_000_000 + 49 * T, map.WallAt(5.0));
        }

        [Fact]
        public void An_operator_pause_is_a_stretch_and_a_sim_seek_into_it_lands_at_its_end()
        {
            // 10 frames run (sim 1.0), 20 paused (frame 15 changes something), 10 run
            var map = Map(Run(10).Concat(Pause(20)).Concat(Run(10)).ToArray(), changed: i => i == 15);

            var s = Assert.Single(map.Stretches);
            Assert.Equal(1.0, s.SimSeconds, 6);
            Assert.True(s.Changed);
            Assert.Equal(1_000_000 + 9 * T, s.StartWallTicks);    // the last frame before the clock stood still
            Assert.Equal(1_000_000 + 30 * T, s.EndWallTicks);     // the frame that ran again

            Assert.Equal(1_000_000 + 29 * T, map.WallAt(1.0));                                        // after: the pause's changes shown
            Assert.Equal(1_000_000 + 9 * T, map.WallAt(1.0, ReplayTimeMap.Edge.BeforeStretch));       // before it
            Assert.Equal(1.0, map.SimAt(1_000_000 + 20 * T), 6);                                      // inside: the paused instant
            Assert.Equal(1_000_000 + 29 * T, map.NextUnpause(1_000_000 + 12 * T));                    // the sim axis jumps there
            Assert.Null(map.NextUnpause(1_000_000 + 35 * T));
        }

        [Fact]
        public void An_unchanged_pause_is_a_hollow_stretch()
        {
            var map = Map(Run(10).Concat(Pause(5)).Concat(Run(10)).ToArray());
            Assert.False(Assert.Single(map.Stretches).Changed);
        }

        /// <summary>⭐ D-H (R-3063) — a stepped run: one frame advances per step, the frames between hold. Those gaps are NOT stretches
        /// (no marks, tens of thousands of them otherwise); only the operator's pause before the first step is.</summary>
        [Fact]
        public void The_gaps_of_a_stepped_run_are_not_stretches()
        {
            var frames = Run(10).Concat(Pause(30)).ToList();                         // run, then the operator pauses
            for (int step = 0; step < 6; step++) frames.AddRange(Run(1).Concat(Pause(8)));   // then steps by hand
            var map = Map(frames.ToArray());

            var s = Assert.Single(map.Stretches);
            Assert.Equal(1.0, s.SimSeconds, 6);
            Assert.Equal(1.6, map.SimAt(long.MaxValue), 6);
        }

        [Fact]
        public void A_v6_index_has_no_sim_axis()
        {
            var map = ReplayTimeMap.FromFrames(new long[] { 1, 2 }, new[] { double.NaN, double.NaN }, new[] { FrameFlags.Unknown, FrameFlags.Unknown });
            Assert.False(map.HasSimTime);
            Assert.True(double.IsNaN(map.SimAt(1)));
        }

        /// <summary>⭐ R3 — a stretch is the cluster's only where EVERY node paused: a node that kept running cuts it.</summary>
        [Fact]
        public void The_clusters_map_keeps_a_stretch_only_where_every_node_paused()
        {
            var a = Map(Run(10).Concat(Pause(20)).Concat(Run(10)).ToArray());
            var b = Map(Run(15).Concat(Pause(10)).Concat(Run(15)).ToArray(), changed: i => i == 18);
            var c = Map(Run(40));   // a node that never paused

            var ab = ReplayTimeMap.Intersect(new[] { a, b });
            var s = Assert.Single(ab.Stretches);
            Assert.Equal(1_000_000 + 14 * T, s.StartWallTicks);
            Assert.Equal(1_000_000 + 25 * T, s.EndWallTicks);
            Assert.True(s.Changed);
            Assert.Same(a.Points, ab.Points);   // the anchor node's sim times

            Assert.Empty(ReplayTimeMap.Intersect(new[] { a, b, c }).Stretches);
        }

        [Fact]
        public void A_long_run_compresses_to_few_keypoints()
        {
            var frames = Run(20_000, 1.0 / 60).Concat(Pause(600)).Concat(Run(20_000, 2.0 / 60)).ToArray();   // 1×, pause, 2×
            var map = Map(frames);
            Assert.True(map.Points.Count <= 8, $"{map.Points.Count} keypoints");
            Assert.Single(map.Stretches);
        }
        /// <summary>
        /// ⭐⭐ CE-4066 — a sim seek to a RECORDED frame's own sim time lands ON that frame: on a run with uneven wall spacing (an HTTP-stepped
        /// one) the fitted line passes some frames up to the tolerance early, and the inverted wall must still not fall before the frame — nor
        /// reach the next. 🔴 Before: inverted at the exact sim, a frame standing after the line was missed and the seek landed one frame short.
        /// </summary>
        [Fact]
        public void CE4066_A_sim_seek_to_a_recorded_frames_sim_lands_on_that_frame_however_uneven_the_wall_spacing()
        {
            var rng = new System.Random(4066);
            int n = 3000;
            var wall = new long[n]; var sim = new double[n]; var flags = new Fdp.Core.FlightRecorder.FrameFlags[n];
            long w = 10_000_000;
            for (int i = 0; i < n; i++)
            {
                sim[i] = i / 60d;
                w += System.TimeSpan.TicksPerSecond / 60 + rng.Next(-30_000, 30_000);   // ±3 ms of wall jitter per frame
                wall[i] = w;
            }
            var map = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, sim, flags);
            int missed = 0;
            for (int i = 1; i < n - 1; i++)
            {
                long at = map.SeekWallAt(sim[i]);
                if (at < wall[i] || at >= wall[i + 1]) missed++;   // on-or-before at `at` must be frame i
            }
            Assert.Equal(0, missed);
        }

    }
}

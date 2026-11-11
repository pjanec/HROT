using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Replay.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1479</c> — the ONE clock → frame rule (<see cref="ReplayClockRule"/>): the frames due at a clock are those whose wall ticks are ≤ it, applied in order and never
    /// more than the clock says; the editor's <see cref="PlaybackTickSystem"/> and the rule agree on the frame for any clock.
    /// </summary>
    public class ReplayClockRuleTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"clockrule_{Guid.NewGuid():N}");
        private const long T0 = 10 * TimeSpan.TicksPerSecond;
        private const int Frames = 20;
        private static long WallOf(int f) => T0 + (long)(f * 1.4 * TimeSpan.TicksPerSecond);   // a frame per ~1.4 s of recorded time (the 1 000-man recording)

        public ReplayClockRuleTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private string Record()
        {
            string path = Path.Combine(_dir, "r.fdp");
            using var repo = new EntityRepository();
            repo.RegisterComponent<Pos>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Pos { X = 0 });
            using var rec = new AsyncRecorder(path);
            uint prev = 0;
            for (int f = 0; f < Frames; f++)
            {
                repo.Tick();
                repo.GetComponentRW<Pos>(e).X = f;
                if (f == 0) rec.CaptureKeyframe(repo, WallOf(f), blocking: true);
                else rec.CaptureFrame(repo, prev, WallOf(f), blocking: true);
                prev = repo.GlobalVersion;
            }
            return path;
        }

        [ComponentId(503)] public struct Pos { public float X; }

        private static EntityRepository World() { var r = new EntityRepository(); r.RegisterComponent<Pos>(); return r; }

        [Fact]
        public void A_OneXOverNSeconds_AppliesTheFramesWhoseTimeIsAtMostTheClock_AndNoMore()
        {
            using var p = new PlaybackController(Record());
            using var w = World();
            int applied = 0;
            for (int tick = 1; tick <= 150; tick++)   // 0.2 s per render frame, 30 s
            {
                long clock = T0 + tick * (TimeSpan.TicksPerSecond / 5);
                int due = ReplayClockRule.FramesDue(p, clock);
                Assert.True(due <= ReplayClockRule.SeekThreshold, "0.2 s never has more than one 1.4 s frame due");
                for (int i = 0; i < due; i++) { Assert.True(p.StepForward(w)); applied++; }
                int expectedCurrent = -1;
                for (int f = 0; f < Frames; f++) if (WallOf(f) <= clock) expectedCurrent = f;
                Assert.Equal(expectedCurrent, p.CurrentFrame);   // exactly the frames due: no more, none missed
            }
            Assert.Equal(Frames, applied);
            // the old pacing (one frame per 1/60 s, up to 10 per render frame) would have applied 10 frames in the FIRST render frame
            Assert.Equal(1, FramesAtFirstTick());
        }

        private int FramesAtFirstTick()
        {
            using var p = new PlaybackController(Record());
            return ReplayClockRule.FramesDue(p, T0 + TimeSpan.TicksPerSecond / 5);   // frame 0 (the keyframe) is due at the very start, nothing else
        }

        [Fact]
        public void B_TheEditorsPlaybackTickSystem_LandsOnTheSameFrame_ForAnyClock()
        {
            string path = Record();
            foreach (double seconds in new[] { 0.0, 0.5, 1.4, 3.0, 4.2, 9.99, 15.0, 26.6, 27.5 })
            {
                long clock = T0 + (long)(seconds * TimeSpan.TicksPerSecond);
                using var ed = new PlaybackController(path);
                using var we = World();
                new PlaybackTickSystem(ed, new Stub(clock, T0)).Execute(we, 0.016f);

                using var br = new PlaybackController(path);
                using var wb = World();
                int due = ReplayClockRule.FramesDue(br, clock);
                if (ReplayClockRule.IsSeekGap(due)) br.SeekToWallClockTicks(wb, clock);   // what the Browser does for a gap
                else for (int i = 0; i < due; i++) br.StepForward(wb);

                Assert.Equal(ed.CurrentFrame, br.CurrentFrame);
            }
        }

        private sealed class Stub : ITimeController
        {
            private GlobalTime _s;
            public Stub(long wall, long start) => _s = new GlobalTime { TotalTime = (wall - start) / (double)TimeSpan.TicksPerSecond, TotalWallTicks = wall };
            public GlobalTime Update() => _s;
            public GlobalTime GetCurrentState() => _s;
            public TimeMode GetMode() => TimeMode.Continuous;
            public void SetTimeScale(float scale) { }
            public float GetTimeScale() => 1f;
            public void SeedState(GlobalTime state) { _s = state; }
            public void Dispose() { }
        }
    }
}

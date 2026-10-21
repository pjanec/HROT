using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Signatures;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3282</c> (Q95 C; docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.3) — the lossless sound ring: a reader gets every sound after its
    /// cursor, oldest first; a reader more than <see cref="SoundEmissionRing.Capacity"/> behind loses the oldest and the loss is COUNTED; a sensor
    /// with no cursor yet reads only what is still ringing.
    /// </summary>
    public class SoundEmissionRingTests
    {
        private static SoundEmission Shot(float x, double time = 0d) => new() { Kind = (byte)SoundKind.Shot, At = new Vector3(x, 0f, 0f), Carry = 100f, Time = time };

        /// <summary>
        /// ⭐ <c>CE-3292</c> (R-3035) — the ring outlives a load and a preview Stop (it is a node singleton). 📐 Measured before: after a reload
        /// every fresh sensor heard the previous runs' shots, and each reload heard more. ⇒ when the clock goes BACK, nothing made before it is
        /// heard again — by a fresh sensor or by one whose cursor was rewound with the world. ✅ Red-proof: the marker removed ⇒ 3 heard ⇒ red.
        /// </summary>
        [Fact]
        public void CE3292_AfterTheClockGoesBack_NoSoundOfTheEarlierTimelineIsHeard()
        {
            var ring = new SoundEmissionRing();
            ring.Tick(9.0);
            for (int i = 1; i <= 3; i++) ring.Append(Shot(i, time: 9.0));   // the previous run's shots
            ring.Tick(0.0);                                                  // a load snaps the clock back
            ring.Tick(0.5);
            Span<SoundEmission> into = stackalloc SoundEmission[8];
            Assert.Equal(0, ring.ReadAfter(0, into, out long last));         // a fresh sensor
            Assert.Equal(0, ring.ReadAfter(1, into, out _));                 // a cursor rewound with the world
            ring.Append(Shot(7, time: 0.5));                                 // this run's first shot
            int n = ring.ReadAfter(last, into, out _);
            Assert.Equal(1, n);
            Assert.Equal(7f, into[0].At.X);
        }

        [Fact]
        public void CE3282_AReader_GetsEverySoundAfterItsCursor_AndResumesWhereItStopped()
        {
            var ring = new SoundEmissionRing();
            for (int i = 1; i <= 5; i++) ring.Append(Shot(i));
            Span<SoundEmission> into = stackalloc SoundEmission[3];
            int n = ring.ReadAfter(1, into, out long last);
            Assert.Equal(3, n);                                     // 2, 3, 4 — the span is full
            Assert.Equal(new[] { 2f, 3f, 4f }, new[] { into[0].At.X, into[1].At.X, into[2].At.X });
            Assert.Equal(4, last);
            n = ring.ReadAfter(last, into, out last);
            Assert.Equal(1, n);                                     // 5 — nothing read twice, nothing skipped
            Assert.Equal(5f, into[0].At.X);
            Assert.Equal(0, ring.ReadAfter(last, into, out _));
            Assert.Equal(0, ring.Overflow);
        }

        /// <summary>✅ Red-proof: the overflow count removed from <c>ReadAfter</c> ⇒ 0 ⇒ red.</summary>
        [Fact]
        public void CE3282_AReaderFarBehind_LosesTheOldest_AndTheLossIsCounted()
        {
            var ring = new SoundEmissionRing();
            for (int i = 1; i <= SoundEmissionRing.Capacity + 10; i++) ring.Append(Shot(i));
            Span<SoundEmission> into = stackalloc SoundEmission[SoundEmissionRing.Capacity];
            int n = ring.ReadAfter(1, into, out long last);          // it last read serial 1; 2…10 were overwritten (11…4106 held)
            Assert.Equal(9, ring.Overflow);
            Assert.Equal(SoundEmissionRing.Capacity, n);
            Assert.Equal(11f, into[0].At.X);                         // the oldest still held
            Assert.Equal(SoundEmissionRing.Capacity + 10, last);
        }

        [Fact]
        public void CE3282_ASensorWithNoCursor_ReadsOnlyWhatIsStillRinging_AndCountsNothingLost()
        {
            var ring = new SoundEmissionRing();
            ring.Tick(100d);
            ring.Append(Shot(1f, time: 90d));                         // ten seconds ago
            ring.Append(Shot(2f, time: 99.8d));                       // still ringing
            Span<SoundEmission> into = stackalloc SoundEmission[4];
            int n = ring.ReadAfter(0, into, out long last, notBefore: 100d - SoundEmissionSystem.SoundLingerSeconds);
            Assert.Equal(1, n);
            Assert.Equal(2f, into[0].At.X);
            Assert.Equal(2, last);
            Assert.Equal(0, ring.Overflow);
        }

        [Fact]
        public void CE3282_ASoundOlderThanTheMaxAge_IsNotHeard()
        {
            var ring = new SoundEmissionRing();
            ring.Append(Shot(1f, time: 0d));
            ring.Tick(SoundEmissionRing.MaxAgeSeconds + 1d);
            Span<SoundEmission> into = stackalloc SoundEmission[4];
            Assert.Equal(0, ring.ReadAfter(0, into, out long last, notBefore: double.MinValue));
            Assert.Equal(1, last);                                    // passed over, not kept for later
        }

        /// <summary>⭐⭐ <c>CE-3293</c> — a reader as of a time reads only what was made before it, and its cursor stops there: the sounds
        /// the writer appends while a background solve runs (the dispatching step's own) are the NEXT solve's, whatever the threads did.
        /// 📐 Measured live (ua-tank-ammo, four loads): one run heard a burst a solve earlier than the other three.</summary>
        [Fact]
        public void CE3293_AReaderAsOfATime_ReadsOnlyWhatCameBefore_AndTheNextReadTakesTheRest()
        {
            var ring = new SoundEmissionRing();
            ring.Tick(1.0);
            ring.Append(Shot(1, time: 1.0));                                 // the step before
            ring.Tick(1.1);
            ring.Append(Shot(2, time: 1.1));                                 // the dispatching step's own, written beside the solve
            Span<SoundEmission> into = stackalloc SoundEmission[8];
            int n = ring.ReadAfter(0, into, out long last, notBefore: 0.0, before: 1.1);
            Assert.Equal(1, n);
            Assert.Equal(1f, into[0].At.X);
            Assert.Equal(1, last);                                           // the cursor waits before the step's own sound
            n = ring.ReadAfter(last, into, out _, before: 1.2);
            Assert.Equal(1, n);
            Assert.Equal(2f, into[0].At.X);                                  // heard by the next solve, once
        }
    }
}

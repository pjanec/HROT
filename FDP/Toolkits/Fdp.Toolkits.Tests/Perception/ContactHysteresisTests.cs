using System;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ The ONE acquired / lost rule (<see cref="ContactHysteresis"/>), on a sensor's own contact list — what the
    /// memory stage runs for every perception sensor. <c>CE-3052</c>: re-homed from the retired
    /// <c>SensorTrackDebounceSystem</c>, which was this rule's second caller.
    /// </summary>
    public class ContactHysteresisTests
    {
        private static (long Id, SensorTrackStatus State)[] Apply(ref SensorContactList list, uint tick)
        {
            Span<(long, SensorTrackStatus)> buf = stackalloc (long, SensorTrackStatus)[PerceptionConstants.MaxTrackedTargets];
            int n = ContactHysteresis.Apply(ref list, tick, buf, out _);
            return buf.Slice(0, n).ToArray();
        }

        /// <summary>Several contacts sighted in the same first tick are ALL acquired, once (the CE-3032 case).</summary>
        [Fact]
        public void SeveralSightingsInOneTick_AreAllAcquired_Once()
        {
            var list = new SensorContactList();
            foreach (long id in new long[] { 10, 20, 30 }) SensorContactList.UpdateSighting(ref list, id, 5);

            var first = Apply(ref list, 5);
            Assert.Equal(new long[] { 10, 20, 30 }, Array.ConvertAll(first, t => t.Id));
            Assert.All(first, t => Assert.Equal(SensorTrackStatus.Acquired, t.State));

            Assert.Empty(Apply(ref list, 6));   // held, not re-announced
        }

        /// <summary>An acquired contact unseen for MORE than the threshold is lost exactly once; seen again, re-acquired.</summary>
        [Fact]
        public void SilenceBeyondTheThreshold_LosesOnce_AndASightingReacquires()
        {
            var list = new SensorContactList();
            SensorContactList.UpdateSighting(ref list, 42, 100);
            Assert.Single(Apply(ref list, 100));

            uint edge = 100 + ContactHysteresis.TrackLostThresholdTicks;
            Assert.Empty(Apply(ref list, edge));                        // exactly at the threshold: still held
            var lost = Assert.Single(Apply(ref list, edge + 1));
            Assert.Equal((42L, SensorTrackStatus.Lost), lost);
            Assert.Empty(Apply(ref list, edge + 2));                    // lost once, not every tick

            SensorContactList.UpdateSighting(ref list, 42, edge + 3);
            Assert.Equal((42L, SensorTrackStatus.Acquired), Assert.Single(Apply(ref list, edge + 3)));
        }

        /// <summary>
        /// ⭐ <c>CE-3260</c> (BR-7, §5.7 AQ-1) — the DWELL: a sighting that needs 2 s is not acquired before it has been seen for 2 s without a
        /// break; a sighting needing 0 is acquired at once (the rule above, unchanged).
        /// </summary>
        [Fact]
        public void CE3260_ASightingWithADwell_IsAcquiredOnlyAfterIt()
        {
            Span<(long, SensorTrackStatus)> buf = stackalloc (long, SensorTrackStatus)[PerceptionConstants.MaxTrackedTargets];
            var list = new SensorContactList();
            SensorContactList.UpdateSighting(ref list, 7, 10, 1.0f, 2f);
            SensorContactList.UpdateSighting(ref list, 8, 10, 1.0f, 0f);
            int n = ContactHysteresis.Apply(ref list, 10, buf, out _, 1.0);
            Assert.Equal((8L, SensorTrackStatus.Acquired), Assert.Single(buf.Slice(0, n).ToArray()));

            SensorContactList.UpdateSighting(ref list, 7, 11, 2.9f, 2f);
            Assert.Equal(0, ContactHysteresis.Apply(ref list, 11, buf, out _, 2.9));
            SensorContactList.UpdateSighting(ref list, 7, 12, 3.0f, 2f);
            n = ContactHysteresis.Apply(ref list, 12, buf, out _, 3.0);
            Assert.Equal((7L, SensorTrackStatus.Acquired), Assert.Single(buf.Slice(0, n).ToArray()));
        }
    }
}

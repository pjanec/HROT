using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3262</c> (DA-14, R-2035; behaviors' sectors note ③) — a near miss crosses from the muscle (the ballistics) to the unit's brain
    /// WITH the round's origin (its muzzle): the bearing a counter-sniper drill reports, and what tightens self-defence (DA-28) to "the
    /// one who shot at me". ⛔ Still no shooter identity. The near-miss wire had no suite before.
    /// </summary>
    public sealed class NearMissTranslatorTests
    {
        /// <summary>✅ Red-proof: the egress dropping the origin ⇒ the brain's event has none ⇒ red.</summary>
        [Fact]
        public void CE3262_ANearMiss_CarriesTheRoundsOrigin_ToTheBrain_MarkedRemote()
        {
            var evt = new NearMissEvent { X = 30f, Y = 2f, Z = 1f, OriginX = 0.5f, OriginY = 0f, OriginZ = 1.4f, HasOrigin = true };
            var sample = Hrot.Network.NED.SimHost.NearMissEgressTranslator.ToSample(77L, in evt);
            Assert.Equal((77L, (byte)1), (sample.UnitEntityId, sample.HasOrigin));

            var unit = new Entity(5, 1);
            var back = Hrot.Network.NED.SimHost.NearMissIngressTranslator.ToEvent(in sample, unit);
            Assert.Equal((unit, true, true), (back.Unit, back.IsRemote, back.HasOrigin));
            Assert.Equal((0.5f, 0f, 1.4f), (back.OriginX, back.OriginY, back.OriginZ));
            Assert.Equal((30f, 2f, 1f), (back.X, back.Y, back.Z));

            var none = Hrot.Network.NED.SimHost.NearMissIngressTranslator.ToEvent(
                Hrot.Network.NED.SimHost.NearMissEgressTranslator.ToSample(77L, new NearMissEvent { X = 1f }), unit);
            Assert.False(none.HasOrigin);   // a round that never knew its muzzle says so
        }
    }
}

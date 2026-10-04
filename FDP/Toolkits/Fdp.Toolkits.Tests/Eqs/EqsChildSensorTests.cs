using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>Rails for <see cref="EqsChildSensor"/>'s epoch stamp (CE-3049).</summary>
    public class EqsChildSensorTests
    {
        /// <summary>
        /// ⭐ CE-3049 — a token of another slot (a doctrine run: high bit set) must not stamp like the behaviour run with
        /// the same low bits. 🔴 The stamp kept only the owner's low 16 bits, so 0x80000005 and 5 stamped alike.
        /// </summary>
        [Fact]
        public void StampOwner_ADoctrineToken_DiffersFromTheBehaviourTokenWithTheSameLowBits_CE3049()
        {
            Assert.NotEqual(EqsChildSensor.StampOwner(1u, 5u), EqsChildSensor.StampOwner(1u, 0x8000_0005u));
            // behaviour tokens below 65 536 stamp exactly as before (no change for existing sensors)
            Assert.Equal((5u << 16) | 1u, EqsChildSensor.StampOwner(1u, 5u));
        }

        /// <summary>⭐ CE-3049 — a refresh that overflows the count wraps the low half and leaves the owner stamp alone.
        /// 🔴 <c>EqsLifecycleNodes</c> did <c>Epoch++</c>, carrying the overflow into the owner bits.</summary>
        [Fact]
        public void NextEpoch_WrapsTheRefreshCount_NeverTouchesTheOwnerStamp_CE3049()
        {
            uint stamped = EqsChildSensor.StampOwner(0xFFFFu, 7u);
            uint next = EqsChildSensor.NextEpoch(stamped);
            Assert.Equal(stamped & 0xFFFF0000u, next & 0xFFFF0000u);
            Assert.Equal(0u, next & 0xFFFFu);
            Assert.Equal(EqsChildSensor.StampOwner(3u, 7u), EqsChildSensor.NextEpoch(EqsChildSensor.StampOwner(2u, 7u)));
        }
    }
}

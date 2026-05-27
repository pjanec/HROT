using CarKinem.Core;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Tests for NAV-P0-T2 — verifies that the KinematicsMode enum extensions
    /// do not break existing values and that the new crowd/naval/flying modes
    /// have the expected byte values.
    /// </summary>
    public class NavigationEnumsTests
    {
        // ── Existing values must not shift ────────────────────────────────────

        [Fact]
        public void KinematicsMode_None_HasValueZero()
        {
            Assert.Equal(0, (byte)KinematicsMode.None);
        }

        [Fact]
        public void KinematicsMode_RoadGraph_HasValueOne()
        {
            Assert.Equal(1, (byte)KinematicsMode.RoadGraph);
        }

        [Fact]
        public void KinematicsMode_CustomTrajectory_HasValueTwo()
        {
            Assert.Equal(2, (byte)KinematicsMode.CustomTrajectory);
        }

        [Fact]
        public void KinematicsMode_Formation_HasValueThree()
        {
            Assert.Equal(3, (byte)KinematicsMode.Formation);
        }

        [Fact]
        public void KinematicsMode_Direct_HasValueFour()
        {
            Assert.Equal(4, (byte)KinematicsMode.Direct);
        }

        // ── New values must start at 5 and not collide with Direct=4 ─────────

        [Fact]
        public void KinematicsMode_Crowd_HasValueFive()
        {
            Assert.Equal(5, (byte)KinematicsMode.Crowd);
        }

        [Fact]
        public void KinematicsMode_Naval_HasValueSix()
        {
            Assert.Equal(6, (byte)KinematicsMode.Naval);
        }

        [Fact]
        public void KinematicsMode_Flying_HasValueSeven()
        {
            Assert.Equal(7, (byte)KinematicsMode.Flying);
        }

        // ── No collision: Crowd != Direct ─────────────────────────────────────

        [Fact]
        public void KinematicsMode_Crowd_DoesNotEqualDirect()
        {
            Assert.NotEqual(KinematicsMode.Direct, KinematicsMode.Crowd);
        }
    }
}

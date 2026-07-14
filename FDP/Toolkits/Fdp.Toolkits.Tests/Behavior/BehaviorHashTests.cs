using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Behavior;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Verifies <see cref="BehaviorHash.FromName"/> is the deterministic, process-stable behavior
    /// identity hash: stable across calls, distinct per name, non-zero for real names, and reserving
    /// 0 as the None sentinel.
    /// </summary>
    public class BehaviorHashTests
    {
        // The behavior names this system registers today (curated + JSON-authored).
        private static readonly string[] KnownBehaviorNames =
        {
            "MoveToLocation", "FollowRoute", "JoinFormation", "Idle", "WanderMilitary",
            "FireAtTarget", "HullDownAttackRun", "PlatoonHillAttack",
        };

        [Fact]
        public void FromName_IsDeterministic_AcrossCalls()
        {
            foreach (var name in KnownBehaviorNames)
                Assert.Equal(BehaviorHash.FromName(name), BehaviorHash.FromName(name));
        }

        [Fact]
        public void FromName_KnownValue_IsStable()
        {
            // Golden values independently computed as FNV-1a-32 over UTF-8 bytes
            // (basis 2166136261, prime 16777619). Pins the algorithm against accidental drift.
            Assert.Equal(unchecked((int)0x499BD7D7u), BehaviorHash.FromName("PlatoonHillAttack"));
            Assert.Equal(unchecked((int)0x39DEFE7Bu), BehaviorHash.FromName("HullDownAttackRun"));
            Assert.Equal(unchecked((int)0xA037816Eu), BehaviorHash.FromName("MoveToLocation"));
        }

        [Fact]
        public void FromName_NullOrEmpty_IsZeroSentinel()
        {
            Assert.Equal(0, BehaviorHash.FromName(null));
            Assert.Equal(0, BehaviorHash.FromName(""));
        }

        [Fact]
        public void FromName_RealNames_AreNonZero()
        {
            foreach (var name in KnownBehaviorNames)
                Assert.NotEqual(0, BehaviorHash.FromName(name));
        }

        [Fact]
        public void FromName_DistinctNames_ProduceDistinctHashes()
        {
            var hashes = KnownBehaviorNames.Select(BehaviorHash.FromName).ToList();
            var distinct = new HashSet<int>(hashes);
            Assert.Equal(hashes.Count, distinct.Count);
        }

        [Fact]
        public void FromName_CaseSensitive()
        {
            Assert.NotEqual(BehaviorHash.FromName("PlatoonHillAttack"),
                            BehaviorHash.FromName("platoonhillattack"));
        }
    }
}

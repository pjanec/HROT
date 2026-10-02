using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// CE-290 (C4) — the creator-local poll-store. A dict keyed by NetworkId with evict-on-read of terminal
    /// results (a lost creation-FAILURE must never be silently overwritten) and a TTL sweep
    /// (DESIGN_Cross_Node_Construction_Barrier.md §3b.4).
    /// </summary>
    public class ConstructionResultsTests
    {
        [Fact]
        public void UnknownId_ReadsPending()
        {
            var store = new ConstructionResults();
            var r = store.Get(networkId: 42);
            Assert.Equal(ConstructionOutcome.Pending, r.Outcome);
            Assert.False(r.IsResolved);
        }

        [Fact]
        public void Pending_StaysPending_AndIsNotEvicted()
        {
            var store = new ConstructionResults();
            store.SetPending(42, frame: 0);
            Assert.Equal(ConstructionOutcome.Pending, store.Get(42).Outcome);
            // Not evicted — a second poll still reads Pending.
            Assert.Equal(ConstructionOutcome.Pending, store.Get(42).Outcome);
            Assert.Equal(1, store.Count);
        }

        [Fact]
        public void Success_IsDeliveredOnce_ThenEvicted()
        {
            var store = new ConstructionResults();
            store.SetPending(42, 0);
            store.Resolve(42, ConstructionOutcome.Success, ConstructionFailReason.None, frame: 10);

            var first = store.Get(42);
            Assert.Equal(ConstructionOutcome.Success, first.Outcome);
            Assert.True(first.IsResolved);

            // Evicted on read — a second poll reads Pending (unknown), the failure-not-silently-lost guarantee.
            Assert.Equal(ConstructionOutcome.Pending, store.Get(42).Outcome);
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void FailedTimeout_CarriesReason_AndIsDeliveredOnce()
        {
            var store = new ConstructionResults();
            store.Resolve(7, ConstructionOutcome.Failed, ConstructionFailReason.Timeout, frame: 5);
            var r = store.Get(7);
            Assert.Equal(ConstructionOutcome.Failed, r.Outcome);
            Assert.Equal(ConstructionFailReason.Timeout, r.Reason);
            Assert.Equal(0, store.Count);   // evicted
        }

        [Fact]
        public void EvictOlderThan_ReclaimsUnpolledEntries_OnlyPastTtl()
        {
            var store = new ConstructionResults();
            store.Resolve(1, ConstructionOutcome.Success, ConstructionFailReason.None, frame: 0);
            store.SetPending(2, frame: 100);

            store.EvictOlderThan(currentFrame: 50, ttlFrames: 60);   // id 1 is 50 old — within TTL, kept
            Assert.Equal(2, store.Count);

            store.EvictOlderThan(currentFrame: 200, ttlFrames: 60);  // id 1 is 200 old, id 2 is 100 old — both past TTL
            Assert.Equal(0, store.Count);
        }
    }
}

using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Network.NED.CGF;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// Tests for <see cref="EqsResultIngressTranslator"/> cache eviction behavior.
    /// OFX-017: NotAliveDisposed DDS samples must remove the corresponding entry from
    /// the internal child-entity cache so the next live sample triggers a fresh scan.
    /// </summary>
    public class EqsResultIngressTranslatorTests
    {
        // OFX-017: cache entry removed on NotAliveDisposed.
        [Fact]
        public void PollIngress_NotAliveDisposed_RemovesCacheEntry()
        {
            // Arrange: create translator with no DDS participant (offline mode).
            var entityMap  = new NetworkEntityMap();
            var translator = new EqsResultIngressTranslator(participant: null, entityMap);

            var cacheKey   = (ParentNetId: 123L, ChildIndex: 1);
            // Seed a synthetic cache entry as if a live sample had already been processed.
            translator._childEntityCache[cacheKey] = new Entity(1UL);

            Assert.True(translator._childEntityCache.ContainsKey(cacheKey),
                "Precondition: cache entry must be present before the disposal signal.");

            // Act: invoke the internal helper that PollIngress calls on !sample.IsValid.
            translator.RemoveCacheEntry(cacheKey.ParentNetId, cacheKey.ChildIndex);

            // Assert: the entry is gone so the next live sample triggers a fresh entity scan.
            Assert.False(translator._childEntityCache.ContainsKey(cacheKey),
                "Cache entry must be evicted on NotAliveDisposed.");
        }

        // Verify that removing a non-existent key is a no-op (no exception).
        [Fact]
        public void RemoveCacheEntry_NonExistentKey_IsNoOp()
        {
            var entityMap  = new NetworkEntityMap();
            var translator = new EqsResultIngressTranslator(participant: null, entityMap);

            // No cache entry seeded -- must not throw.
            translator.RemoveCacheEntry(parentNetworkId: 999L, localChildIndex: 7);

            Assert.Empty(translator._childEntityCache);
        }
    }
}

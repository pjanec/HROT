using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>
    /// Tests for <see cref="FederatedGuidResolver"/> (RBF-P3T2).
    /// </summary>
    public sealed class FederatedGuidResolverTests
    {
        // ── RBF-P3T2 ─────────────────────────────────────────────────────────

        /// <summary>
        /// Resolve(Entity) returns the pre-computed string when the entity is in the save map.
        /// </summary>
        [Fact]
        public void RBF_P3T2_SaveMap_Hit_ReturnsGuidString()
        {
            var entity = new Entity(1, 0);
            const string guid = "11111111-2222-3333-4444-555555555555";
            var resolver = new FederatedGuidResolver();
            resolver.SetSaveMap(new Dictionary<Entity, string> { [entity] = guid });

            Assert.Equal(guid, resolver.Resolve(entity));
        }

        /// <summary>
        /// Resolve(Entity) returns the literal string "null" when the entity is not in the
        /// save map rather than throwing.
        /// </summary>
        [Fact]
        public void RBF_P3T2_SaveMap_Miss_ReturnsNullLiteral()
        {
            var resolver = new FederatedGuidResolver();
            resolver.SetSaveMap(new Dictionary<Entity, string>());

            Assert.Equal("null", resolver.Resolve(new Entity(99, 0)));
        }

        /// <summary>
        /// Resolve(string) returns the mapped Entity when the key is in the load map.
        /// </summary>
        [Fact]
        public void RBF_P3T2_LoadMap_Hit_ReturnsEntity()
        {
            var entity = new Entity(42, 3);
            const string key = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            var resolver = new FederatedGuidResolver();
            resolver.SetLoadMap(new Dictionary<string, Entity> { [key] = entity });

            var result = resolver.Resolve(key);
            Assert.Equal(entity, result);
        }

        /// <summary>
        /// Resolve(string) returns Entity.Null on a cache miss instead of throwing.
        /// </summary>
        [Fact]
        public void RBF_P3T2_LoadMap_Miss_ReturnsEntityNull()
        {
            var resolver = new FederatedGuidResolver();
            resolver.SetLoadMap(new Dictionary<string, Entity>());

            var result = resolver.Resolve("nonexistent-key");
            Assert.Equal(Entity.Null, result);
        }

        /// <summary>
        /// After a hot-swap via SetLoadMap the resolver uses the new map immediately.
        /// </summary>
        [Fact]
        public void RBF_P3T2_HotSwapLoadMap_UsesNewMap()
        {
            var entity1 = new Entity(1, 0);
            var entity2 = new Entity(2, 0);
            const string key = "00000000-0000-0000-0000-000000000001";

            var resolver = new FederatedGuidResolver();
            resolver.SetLoadMap(new Dictionary<string, Entity> { [key] = entity1 });
            Assert.Equal(entity1, resolver.Resolve(key));

            // Hot-swap to a different map.
            resolver.SetLoadMap(new Dictionary<string, Entity> { [key] = entity2 });
            Assert.Equal(entity2, resolver.Resolve(key));
        }
    }
}

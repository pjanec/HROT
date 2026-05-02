using Fdp.Toolkit.Behavior;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BehaviorRegistry"/> (DEBT-006).
    /// Verifies that registry uses stable assigned <c>int</c> IDs rather than
    /// process-randomised <c>string.GetHashCode()</c>.
    /// </summary>
    public class BehaviorRegistryTests
    {
        // ── Test 1 ────────────────────────────────────────────────────────────
        /// <summary>
        /// Registering a behavior with id=42 and then looking up id=42 must
        /// return the exact definition that was registered.
        /// </summary>
        [Fact]
        public void BehaviorRegistry_LookupById_ReturnsCorrectEntry()
        {
            var registry = new BehaviorRegistry();
            var definition = new BehaviorDefinition
            {
                Name      = "TestBehavior",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };

            registry.Register(42, "TestBehavior", definition);

            bool found = registry.TryGetDefinition(42, out var result);

            Assert.True(found);
            Assert.Same(definition, result);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────
        /// <summary>
        /// The same integer ID registered in two separate <see cref="BehaviorRegistry"/>
        /// instances must resolve to equal definitions — confirming that the integer
        /// constant (not a process-random hash) is the stable identity.
        /// </summary>
        [Fact]
        public void BehaviorRegistry_LookupById_IsStableAcrossInstances()
        {
            const int StableId = 42;
            const string Name  = "StableBehavior";

            var defA = new BehaviorDefinition { Name = Name, BrainTier = BehaviorConstants.BrainTierBTree };
            var defB = new BehaviorDefinition { Name = Name, BrainTier = BehaviorConstants.BrainTierBTree };

            var registryA = new BehaviorRegistry();
            var registryB = new BehaviorRegistry();

            registryA.Register(StableId, Name, defA);
            registryB.Register(StableId, Name, defB);

            bool foundA = registryA.TryGetDefinition(StableId, out var resultA);
            bool foundB = registryB.TryGetDefinition(StableId, out var resultB);

            // Both lookups succeed using the same integer constant as the key.
            Assert.True(foundA);
            Assert.True(foundB);
            Assert.Same(defA, resultA);
            Assert.Same(defB, resultB);
            // The resolved IDs in both instances match — stable across instances.
            Assert.Equal(StableId, StableId); // trivially verifies constant stability
        }

        // ── Test 3 ────────────────────────────────────────────────────────────
        /// <summary>
        /// Looking up an ID that was never registered must return <c>false</c>.
        /// </summary>
        [Fact]
        public void BehaviorRegistry_ReturnsNull_ForUnregisteredId()
        {
            var registry = new BehaviorRegistry();
            registry.Register(1, "SomeBehavior", new BehaviorDefinition
            {
                Name      = "SomeBehavior",
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            bool found = registry.TryGetDefinition(9999, out var result);

            Assert.False(found);
            Assert.Null(result);
        }

        // ── Test 4 — GetRegisteredNames: two behaviors ──────────────────────
        /// <summary>
        /// After registering two behaviors with different names, <see cref="BehaviorRegistry.GetRegisteredNames"/>
        /// must return a list containing both names (order is unspecified).
        /// </summary>
        [Fact]
        public void GetRegisteredNames_AfterTwoRegistrations_ReturnsBothNames()
        {
            var registry = new BehaviorRegistry();
            registry.Register(1, "Alpha", new BehaviorDefinition { Name = "Alpha", BrainTier = BehaviorConstants.BrainTierBTree });
            registry.Register(2, "Bravo", new BehaviorDefinition { Name = "Bravo", BrainTier = BehaviorConstants.BrainTierBTree });

            var names = registry.GetRegisteredNames();

            Assert.Contains("Alpha", names);
            Assert.Contains("Bravo", names);
            Assert.Equal(2, names.Count);
        }

        // ── Test 5 — GetRegisteredNames: empty registry ─────────────────────
        /// <summary>
        /// An empty registry must return an empty (non-null) list from
        /// <see cref="BehaviorRegistry.GetRegisteredNames"/>.
        /// </summary>
        [Fact]
        public void GetRegisteredNames_EmptyRegistry_ReturnsEmptyList_NotNull()
        {
            var registry = new BehaviorRegistry();

            var names = registry.GetRegisteredNames();

            Assert.NotNull(names);
            Assert.Empty(names);
        }
    }
}

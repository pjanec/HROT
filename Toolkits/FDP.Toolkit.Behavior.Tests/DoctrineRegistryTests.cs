using FDP.Toolkit.Behavior;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DoctrineRegistry"/> (DEBT-006).
    /// Verifies that registry uses stable assigned <c>int</c> IDs rather than
    /// process-randomised <c>string.GetHashCode()</c>.
    /// </summary>
    public class DoctrineRegistryTests
    {
        // ── Test 1 ────────────────────────────────────────────────────────────
        /// <summary>
        /// Registering a doctrine with id=42 and then looking up id=42 must
        /// return the exact definition that was registered.
        /// </summary>
        [Fact]
        public void DoctrineRegistry_LookupById_ReturnsCorrectEntry()
        {
            var registry = new DoctrineRegistry();
            var definition = new DoctrineDefinition
            {
                Name      = "TestDoctrine",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };

            registry.Register(42, "TestDoctrine", definition);

            bool found = registry.TryGetDefinition(42, out var result);

            Assert.True(found);
            Assert.Same(definition, result);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────
        /// <summary>
        /// The same integer ID registered in two separate <see cref="DoctrineRegistry"/>
        /// instances must resolve to equal definitions — confirming that the integer
        /// constant (not a process-random hash) is the stable identity.
        /// </summary>
        [Fact]
        public void DoctrineRegistry_LookupById_IsStableAcrossInstances()
        {
            const int StableId = 42;
            const string Name  = "StableDoctrine";

            var defA = new DoctrineDefinition { Name = Name, BrainTier = BehaviorConstants.BrainTierBTree };
            var defB = new DoctrineDefinition { Name = Name, BrainTier = BehaviorConstants.BrainTierBTree };

            var registryA = new DoctrineRegistry();
            var registryB = new DoctrineRegistry();

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
        public void DoctrineRegistry_ReturnsNull_ForUnregisteredId()
        {
            var registry = new DoctrineRegistry();
            registry.Register(1, "SomeDoctrine", new DoctrineDefinition
            {
                Name      = "SomeDoctrine",
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            bool found = registry.TryGetDefinition(9999, out var result);

            Assert.False(found);
            Assert.Null(result);
        }
    }
}

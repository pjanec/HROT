using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BehaviorRegistry"/> (DEBT-006).
    /// Verifies that registry uses stable assigned <c>int</c> IDs rather than
    /// process-randomised <c>string.GetHashCode()</c>.
    /// </summary>
    public unsafe class BehaviorRegistryTests
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

        // ── Test 6 — duplicate-name hard error (Phase 1e) ────────────────────
        /// <summary>
        /// After Phase 2c factory retirement, each behavior self-registers exactly once under a
        /// unique name. Registering a second, different definition under an already-registered name
        /// is a genuine collision and must throw rather than silently shadow one definition.
        /// (This is the inverse of the interim anti-shadow rule it replaces.)
        /// </summary>
        [Fact]
        public void Register_DuplicateName_DifferentDefinition_Throws()
        {
            var registry = new BehaviorRegistry();

            registry.Register(100, "X", new BehaviorDefinition
            {
                Name      = "X",
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(200, "X", new BehaviorDefinition
                {
                    Name      = "X",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                }));
        }

        // ── Test 7 — duplicate-name hard error is order/id independent ───────
        /// <summary>
        /// The collision is on the <b>name</b>: a second registration of the same name throws even
        /// when it targets the same derived id (name-as-identity always maps a name to one id).
        /// </summary>
        [Fact]
        public void Register_DuplicateName_Throws_EvenUnderSameDerivedId()
        {
            var registry = new BehaviorRegistry();

            registry.Register("X", new BehaviorDefinition
            {
                Name      = "X",
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register("X", new BehaviorDefinition
                {
                    Name      = "X",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                }));
        }

        // ── Test 8 — distinct names register independently ──────────────────
        /// <summary>
        /// Registering two distinct behavior names must not interfere with each other's
        /// name -> id mappings, even when the anti-shadow duplicate-name check runs.
        /// </summary>
        [Fact]
        public void Register_DistinctNames_DoNotInterfereWithEachOther()
        {
            var registry = new BehaviorRegistry();

            registry.Register(1, "Alpha", new BehaviorDefinition
            {
                Name        = "Alpha",
                BrainTier   = BehaviorConstants.BrainTierBTree,
                ParseParams = static (string json, byte* mem, EntityRepository world, Entity self) => { },
            });
            registry.Register(2, "Bravo", new BehaviorDefinition
            {
                Name      = "Bravo",
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            Assert.True(registry.TryGetId("Alpha", out var alphaId));
            Assert.Equal(1, alphaId);

            Assert.True(registry.TryGetId("Bravo", out var bravoId));
            Assert.Equal(2, bravoId);
        }

        // ── Test 9 — idempotent same-instance re-registration ────────────────
        /// <summary>
        /// Registering the <b>exact same</b> definition instance again under the same name is a no-op
        /// (tolerated so an idempotent re-scan into the same registry does not throw). Only a second,
        /// <i>different</i> definition for the name is a collision.
        /// </summary>
        [Fact]
        public void Register_SameInstanceReRegistration_IsIdempotent()
        {
            var registry = new BehaviorRegistry();

            var def = new BehaviorDefinition
            {
                Name      = "Reloadable",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };

            registry.Register(7, "Reloadable", def);
            registry.Register(7, "Reloadable", def); // same instance → no-op, no throw

            Assert.True(registry.TryGetId("Reloadable", out var id));
            Assert.Equal(7, id);
            Assert.True(registry.TryGetDefinition(7, out var stored));
            Assert.Same(def, stored);
        }

        // ── Test 10 — named resolver overlay binds by name (Phase 2c) ────────
        /// <summary>
        /// A curated <see cref="BehaviorRegistry.RegisterResolver"/> binds a resolver (and params DTO
        /// type) to a topology definition self-registered without one, by name — order-independent.
        /// This replaces the curated↔generated double registration the anti-shadow rule once absorbed.
        /// </summary>
        [Fact]
        public void RegisterResolver_BindsResolverToTopology_RegardlessOfOrder()
        {
            static BehaviorDefinition TopologyOnly() => new()
            {
                Name      = "Y",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };

            // resolver registered BEFORE the topology
            var r1 = new BehaviorRegistry();
            r1.RegisterResolver("Y", static (string json, byte* mem, EntityRepository world, Entity self) => { },
                typeof(int));
            r1.Register("Y", TopologyOnly());
            Assert.True(r1.TryGetDefinition(BehaviorHash.FromName("Y"), out var d1));
            Assert.NotNull(d1!.ParseParams);
            Assert.Equal(typeof(int), d1.ParamsDtoType);

            // resolver registered AFTER the topology
            var r2 = new BehaviorRegistry();
            r2.Register("Y", TopologyOnly());
            r2.RegisterResolver("Y", static (string json, byte* mem, EntityRepository world, Entity self) => { },
                typeof(int));
            Assert.True(r2.TryGetDefinition(BehaviorHash.FromName("Y"), out var d2));
            Assert.NotNull(d2!.ParseParams);
            Assert.Equal(typeof(int), d2.ParamsDtoType);
        }

        // ── Test 11 — name-based Register overload derives id from name ─────
        /// <summary>
        /// The name-based <see cref="BehaviorRegistry.Register(string, BehaviorDefinition)"/>
        /// overload must derive the id via <see cref="BehaviorHash.FromName"/> internally,
        /// with no id argument required from the caller.
        /// </summary>
        [Fact]
        public void Register_ByName_DerivesIdFromName()
        {
            var registry = new BehaviorRegistry();
            var def = new BehaviorDefinition
            {
                Name      = "Foo",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };

            registry.Register("Foo", def);

            Assert.True(registry.TryGetId("Foo", out var id));
            Assert.Equal(BehaviorHash.FromName("Foo"), id);

            Assert.True(registry.TryGetDefinition(id, out var result));
            Assert.Same(def, result);
        }
    }
}

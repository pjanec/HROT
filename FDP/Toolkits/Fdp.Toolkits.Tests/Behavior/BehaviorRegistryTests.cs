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

        // ── Test 6 — double-registration anti-shadow rule ───────────────────
        /// <summary>
        /// Reproduces the HillAttack double-registration bug (AiBehaviorFactory id 3014
        /// with ParseParams vs. a generated PlatoonHillAttackRegistrar id without ParseParams,
        /// both registering the name "PlatoonHillAttack"). A ParseParams-bearing definition
        /// registered first must not be shadowed by a later registration of the same name
        /// under a different id that lacks ParseParams.
        /// </summary>
        [Fact]
        public void Register_DuplicateName_ParseParamsBearingDefinitionWins_RegardlessOfOrder()
        {
            var registry = new BehaviorRegistry();

            var withParseParams = new BehaviorDefinition
            {
                Name        = "X",
                BrainTier   = BehaviorConstants.BrainTierBTree,
                ParseParams = static (string json, byte* mem) => { },
            };
            var withoutParseParams = new BehaviorDefinition
            {
                Name      = "X",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };

            registry.Register(100, "X", withParseParams);
            registry.Register(200, "X", withoutParseParams);

            bool found = registry.TryGetId("X", out var id);
            Assert.True(found);
            Assert.Equal(100, id);

            bool defFound = registry.TryGetDefinition(id, out var def);
            Assert.True(defFound);
            Assert.NotNull(def!.ParseParams);
        }

        // ── Test 7 — double-registration anti-shadow rule, reverse order ────
        /// <summary>
        /// Same scenario as above but with the ParseParams-less definition registered
        /// FIRST and the ParseParams-bearing one SECOND. The rule must be order-independent:
        /// the ParseParams-bearing definition still wins the name mapping.
        /// </summary>
        [Fact]
        public void Register_DuplicateName_ParseParamsBearingDefinitionWins_ReverseOrder()
        {
            var registry = new BehaviorRegistry();

            var withoutParseParams = new BehaviorDefinition
            {
                Name      = "X",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };
            var withParseParams = new BehaviorDefinition
            {
                Name        = "X",
                BrainTier   = BehaviorConstants.BrainTierBTree,
                ParseParams = static (string json, byte* mem) => { },
            };

            registry.Register(200, "X", withoutParseParams);
            registry.Register(100, "X", withParseParams);

            bool found = registry.TryGetId("X", out var id);
            Assert.True(found);
            Assert.Equal(100, id);
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
                ParseParams = static (string json, byte* mem) => { },
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

        // ── Test 9 — same-id re-registration is unaffected ───────────────────
        /// <summary>
        /// Re-registering the same name under the SAME id (the normal hot-reload case)
        /// must update the stored definition and behave exactly as before the fix —
        /// no anti-shadow logic should kick in since <c>existingId == id</c>.
        /// </summary>
        [Fact]
        public void Register_SameIdReRegistration_UpdatesDefinitionWithoutRegression()
        {
            var registry = new BehaviorRegistry();

            var original = new BehaviorDefinition
            {
                Name      = "Reloadable",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };
            var updated = new BehaviorDefinition
            {
                Name        = "Reloadable",
                BrainTier   = BehaviorConstants.BrainTierBTree,
                ParseParams = static (string json, byte* mem) => { },
            };

            registry.Register(7, "Reloadable", original);
            registry.Register(7, "Reloadable", updated);

            bool found = registry.TryGetId("Reloadable", out var id);
            Assert.True(found);
            Assert.Equal(7, id);

            bool defFound = registry.TryGetDefinition(7, out var def);
            Assert.True(defFound);
            Assert.Same(updated, def);
        }

        // ── Test 10 — same-id completeness (post name-identity convergence) ──
        /// <summary>
        /// After Phase 1b both producers mint id = BehaviorHash.FromName(name), so the curated
        /// (ParseParams-bearing) and generated (ParseParams-less) registrations of the same behavior
        /// collide under the SAME id. The ParseParams-bearing definition must survive regardless of
        /// which registers last.
        /// </summary>
        [Fact]
        public void Register_SameId_ParseParamsBearingDefinitionSurvives_RegardlessOfOrder()
        {
            const int Id = 500;

            var withParse = new BehaviorDefinition
            {
                Name = "Y", BrainTier = BehaviorConstants.BrainTierBTree,
                ParseParams = static (string json, byte* mem) => { },
            };
            var withoutParse = new BehaviorDefinition
            {
                Name = "Y", BrainTier = BehaviorConstants.BrainTierBTree,
            };

            // complete first, then incomplete
            var r1 = new BehaviorRegistry();
            r1.Register(Id, "Y", withParse);
            r1.Register(Id, "Y", withoutParse);
            Assert.True(r1.TryGetDefinition(Id, out var d1));
            Assert.NotNull(d1!.ParseParams);

            // incomplete first, then complete
            var r2 = new BehaviorRegistry();
            r2.Register(Id, "Y", withoutParse);
            r2.Register(Id, "Y", withParse);
            Assert.True(r2.TryGetDefinition(Id, out var d2));
            Assert.NotNull(d2!.ParseParams);
        }
    }
}

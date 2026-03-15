using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using Xunit;
using CarKinem.Core;
using CarKinem.Formation;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the domain-specific component registries introduced by MOD1-P3T2.
    /// </summary>
    public class ComponentRegistryTests
    {
        // ── CognitiveComponentRegistry ────────────────────────────────────────

        [Fact]
        public void CognitiveComponentRegistry_RegisterAll_DoesNotThrow()
        {
            using var world = new EntityRepository();
            // RegisterAll must be idempotent and not throw on a fresh world.
            var ex = Record.Exception(() => CognitiveComponentRegistry.RegisterAll(world));
            Assert.Null(ex);
        }

        [Fact]
        public void CognitiveComponentRegistry_RegisterAll_RegistersNavigationIntent()
        {
            using var world = new EntityRepository();
            CognitiveComponentRegistry.RegisterAll(world);

            // NavigationIntent must be queryable (non-null table = registered).
            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationIntent>()));
        }

        [Fact]
        public void CognitiveComponentRegistry_RegisterAll_RegistersBrainHsmComponents()
        {
            using var world = new EntityRepository();
            CognitiveComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<BrainHsm128>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<BrainHsm64>()));
        }

        // ── KinematicComponentRegistry ────────────────────────────────────────

        [Fact]
        public void KinematicComponentRegistry_RegisterAll_DoesNotThrow()
        {
            using var world = new EntityRepository();
            var ex = Record.Exception(() => KinematicComponentRegistry.RegisterAll(world));
            Assert.Null(ex);
        }

        [Fact]
        public void KinematicComponentRegistry_RegisterAll_RegistersNavigationStatus()
        {
            using var world = new EntityRepository();
            KinematicComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationStatus>()));
        }

        [Fact]
        public void KinematicComponentRegistry_RegisterAll_RegistersVehicleComponents()
        {
            using var world = new EntityRepository();
            KinematicComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<VehicleState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<VehicleParams>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<NavState>()));
        }

        [Fact]
        public void KinematicComponentRegistry_RegisterAll_RegistersFormationComponents()
        {
            using var world = new EntityRepository();
            KinematicComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<FormationMember>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<FormationRoster>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<FormationTarget>()));
        }

        // ── CombatComponentRegistry ───────────────────────────────────────────

        [Fact]
        public void CombatComponentRegistry_RegisterAll_DoesNotThrow()
        {
            using var world = new EntityRepository();
            var ex = Record.Exception(() => CombatComponentRegistry.RegisterAll(world));
            Assert.Null(ex);
        }

        [Fact]
        public void CombatComponentRegistry_RegisterAll_RegistersCombatPerceptionComponents()
        {
            using var world = new EntityRepository();
            CombatComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<Faction>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<PerceptionReceptor>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<TargetMemory>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<WeaponState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<Health>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<PhysicsCollider>()));
        }

        // ── SimHostComponentRegistry (idempotency via delegation) ─────────────

        [Fact]
        public void SimHostComponentRegistry_RegisterAll_StillProvidesCognitiveComponents()
        {
            using var world = new EntityRepository();
            // The refactored SimHostComponentRegistry delegates to sub-registries.
            // Verify the full set of components remains accessible.
            SimHostComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<DoctrineState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationIntent>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationStatus>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<VehicleState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<Faction>()));
        }
    }
}

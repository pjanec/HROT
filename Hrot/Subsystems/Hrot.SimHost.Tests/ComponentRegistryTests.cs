using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Map.Common;
using Xunit;
using CarKinem.Core;
using CarKinem.Formation;

namespace Hrot.SimHost.Tests
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

            Assert.Null(Record.Exception(() => world.GetComponentTable<FormationFollower>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<FormationController>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<FormationTarget>()));
        }

        [Fact]
        public void KinematicComponentRegistry_RegisterAll_RegistersNavigationEvents()
        {
            using var world = new EntityRepository();
            KinematicComponentRegistry.RegisterAll(world);

            var ex = Record.Exception(() => world.Events.Publish(new MoveStartedEvent()));
            Assert.Null(ex);
        }

        [Fact]
        public void MuscleRoleComponentRegistry_RegisterAll_RegistersNavigationIntent()
        {
            using var world = new EntityRepository();
            MuscleRoleComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationIntent>()));
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
            HrotSharedComponentRegistry.RegisterAll(world);
            CombatComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<EntityInfo>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<PerceptionReceptor>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<TargetMemory>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<WeaponState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<Health>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<PhysicsCollider>()));
        }

        [Fact]
        public void HierarchyComponentRegistry_RegisterAll_RegistersHierarchyComponents()
        {
            using var world = new EntityRepository();
            HierarchyComponentRegistry.RegisterAll(world);

            Assert.NotNull(world.GetComponentTable<UnitRoster>());
            Assert.NotNull(world.GetComponentTable<UnitSubordinate>());
        }

        [Fact]
        public void NavigationSolverComponentRegistry_RegisterAll_RegistersSolverState()
        {
            using var world = new EntityRepository();
            NavigationSolverComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetSingleton<PathfindingBatchData>()));
            Assert.Null(Record.Exception(() => world.GetSingleton<AreaQueryBatchData>()));
            Assert.Null(Record.Exception(() => world.GetSingleton<EqsTargetPool>()));
        }

        // ── SimHostComponentRegistry (idempotency via delegation) ─────────────

        [Fact]
        public void SimHostComponentRegistry_RegisterAll_StillProvidesCognitiveComponents()
        {
            using var world = new EntityRepository();
            // The refactored SimHostComponentRegistry delegates to sub-registries.
            // Verify the full set of components remains accessible.
            SimHostComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<BehaviorState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationIntent>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationStatus>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<VehicleState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<EntityInfo>()));

            // CS023: UnitRoster and UnitSubordinate must be registered.
            Assert.NotNull(world.GetComponentTable<UnitRoster>());
            Assert.NotNull(world.GetComponentTable<UnitSubordinate>());
        }

        /// <summary>
        /// CS023: After registering all components, every registered component ID
        /// must be unique — no two types share the same ID.
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_RegisterAll_ComponentIdsAreUnique()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            var ids = ComponentTypeRegistry.GetAllTypeIds();
            var uniqueCount = new System.Collections.Generic.HashSet<int>(ids).Count;
            Assert.Equal(ids.Length, uniqueCount);
        }
    }
}

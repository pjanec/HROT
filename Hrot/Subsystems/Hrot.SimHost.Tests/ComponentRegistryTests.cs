using Fdp.Core.Collections;
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
            try
            {
                Assert.Null(Record.Exception(() => world.GetSingleton<PathfindingBatchData>()));
                Assert.Null(Record.Exception(() => world.GetSingleton<AreaQueryBatchData>()));
                Assert.Null(Record.Exception(() => world.GetSingleton<EqsTargetPool>()));
            }
            finally
            {
                NavigationSolverComponentRegistry.DisposeAll(world);
            }
        }

        /// <summary>
        /// <b><c>B3</c> — the registry allocates its four persistent pools AT MOST ONCE per world.</b>
        ///
        /// <para>Two production hosts call this registry twice on one world — <c>EditorSubsystem</c> and
        /// <c>EditorStrideSubsystem</c> each run <c>SimHostComponentRegistry.RegisterAll</c> and
        /// <c>CgfComponentRegistry.RegisterAll</c> on the same world, and both delegate here. Because
        /// <c>SetSingleton</c> is "set or update", an unguarded second call replaced each pool with a fresh
        /// <c>Allocator.Persistent</c> array and orphaned the first — silently, with no test.</para>
        ///
        /// <para>The observable is the array's identity: after a second RegisterAll the world must still
        /// hold the SAME native memory, which is what proves nothing was leaked behind it.</para>
        /// </summary>
        [Fact]
        public void NavigationSolverComponentRegistry_RegisterAll_IsIdempotentOnTheFourPersistentPools()
        {
            using var world = new EntityRepository();
            NavigationSolverComponentRegistry.RegisterAll(world);
            try
            {
                var firstPathfinding = BaseAddress(world.GetSingleton<PathfindingBatchData>().Results);
                var firstAreaQuery   = BaseAddress(world.GetSingleton<AreaQueryBatchData>().Results);
                var firstTargets     = BaseAddress(world.GetSingleton<EqsTargetPool>().Targets);
                var firstResults     = BaseAddress(world.GetSingleton<EqsResultPool>().Results);

                // The second host's registration pass.
                NavigationSolverComponentRegistry.RegisterAll(world);

                Assert.Equal(firstPathfinding, BaseAddress(world.GetSingleton<PathfindingBatchData>().Results));
                Assert.Equal(firstAreaQuery,   BaseAddress(world.GetSingleton<AreaQueryBatchData>().Results));
                Assert.Equal(firstTargets,     BaseAddress(world.GetSingleton<EqsTargetPool>().Targets));
                Assert.Equal(firstResults,     BaseAddress(world.GetSingleton<EqsResultPool>().Results));
            }
            finally
            {
                NavigationSolverComponentRegistry.DisposeAll(world);
            }
        }

        /// <summary>
        /// <c>DisposeAll</c> is the symmetric counterpart <c>RegisterAll</c> never had: three of the four
        /// pools had no production disposer at all. It must clear the stored handles so a second call —
        /// or a later <c>EqsModule.Dispose</c> on the same world — is a no-op rather than a double free.
        /// </summary>
        [Fact]
        public void NavigationSolverComponentRegistry_DisposeAll_FreesEveryPoolAndIsIdempotent()
        {
            using var world = new EntityRepository();
            NavigationSolverComponentRegistry.RegisterAll(world);

            NavigationSolverComponentRegistry.DisposeAll(world);

            Assert.False(world.GetSingleton<PathfindingBatchData>().Results.IsCreated);
            Assert.False(world.GetSingleton<AreaQueryBatchData>().Results.IsCreated);
            Assert.False(world.GetSingleton<EqsTargetPool>().Targets.IsCreated);
            Assert.False(world.GetSingleton<EqsResultPool>().Results.IsCreated);

            // A double free would corrupt the allocator, so this second call is the real assertion.
            NavigationSolverComponentRegistry.DisposeAll(world);
        }

        /// <summary>And a world that never reached the registry must not throw.</summary>
        [Fact]
        public void NavigationSolverComponentRegistry_DisposeAll_ToleratesAWorldWithNoPools()
        {
            using var world = new EntityRepository();
            NavigationSolverComponentRegistry.DisposeAll(world);
        }

        /// <summary>
        /// Address of the array's first element — the identity of the underlying allocation.
        /// <c>NativeArray</c> exposes no pointer accessor, but its indexer returns a <c>ref</c> into the
        /// block, so the address of slot 0 is the block's base address.
        /// </summary>
        private static unsafe nint BaseAddress<T>(NativeArray<T> array) where T : unmanaged
        {
            Assert.True(array.IsCreated);
            ref T slot0 = ref array[0];
            return (nint)System.Runtime.CompilerServices.Unsafe.AsPointer(ref slot0);
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

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

        // ── PerceptionRoleComponentRegistry ───────────────────────────────────
        // 📄 docs/DESIGN_Role_Affinity_Ownership.md §3.9a/§3.9b. Extracted 2026-09-12 (CE-259bf):
        //    Perception was the ONE role with no registry, and its components were filed inside the
        //    BRAIN's — which is why SimHost, a node that runs no cognitive system, had to call the
        //    Brain's registry to get its own role's components.

        [Fact]
        public void PerceptionRoleComponentRegistry_RegisterAll_DoesNotThrow()
        {
            using var world = new EntityRepository();
            var ex = Record.Exception(() => PerceptionRoleComponentRegistry.RegisterAll(world));
            Assert.Null(ex);
        }

        [Fact]
        public void PerceptionRoleComponentRegistry_RegistersTheEqsSet()
        {
            using var world = new EntityRepository();
            PerceptionRoleComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<EqsSensor>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<EqsCognitiveBuffer>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<SensorEvalState>()));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE EXTRACTION RAIL — the host's registered set is UNCHANGED.</b>
        ///
        /// <para>⛔⛔ This is the one that matters. Moving the EQS set out of
        /// <c>CognitiveComponentRegistry</c> is only safe if every host that USED to get it still does.
        /// ⚠ A host loses EQS silently: nothing throws at registration, the solver simply finds no
        /// components and every sensor query returns empty — the failure surfaces as "perception does
        /// nothing", frames later and far from the cause.</para>
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_StillRegistersTheEqsSet_AfterTheExtraction()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<EqsSensor>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<EqsCognitiveBuffer>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<SensorEvalState>()));
        }

        /// <summary>
        /// ⭐⭐ <b>The structural claim: the BRAIN's registry no longer owns PERCEPTION.</b>
        ///
        /// <para>⭐ Asserted as an ABSENCE on the cognitive set alone — ⛔ not on a host, because every
        /// host still composes both registries. ⚠ If someone re-adds the EQS set here "to fix"
        /// something, this reddens and points at the real fix: call the Perception registry.</para>
        /// </summary>
        [Fact]
        public void CognitiveComponentRegistry_NoLongerRegistersPerceptionComponents()
        {
            using var world = new EntityRepository();
            CognitiveComponentRegistry.RegisterAll(world);

            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<EqsSensor>());

            // ⛔ Anti-vacuity: the cognitive set itself must still be there, or this would pass on a
            //    registry that had been emptied entirely.
            Assert.Null(Record.Exception(() => world.GetComponentTable<BrainHsm128>()));
        }

        // ── CE-259bf slice 2: the strays move to homes BOTH hosts already compose ─────

        [Fact]
        public void MissionComponentRegistry_RegistersMissionPlanQueue_AfterTheMove()
        {
            using var world = new EntityRepository();
            MissionComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<MissionPlanQueue>()));
        }

        [Fact]
        public void CombatComponentRegistry_RegistersActorCapabilityState_AfterTheMove()
        {
            using var world = new EntityRepository();
            CombatComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<ActorCapabilityState>()));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The behaviour-preserving claim for slice 2, asserted on the HOST.</b>
        ///
        /// <para>⛔ Moving a component between registries is only safe if every host that used to get it
        /// still does. ⚠ Same silent-failure shape as the EQS move: nothing throws, the component simply
        /// never exists, and the wire translator that writes <c>MissionPlanQueue</c> or the damage system
        /// that reads <c>ActorCapabilityState</c> quietly does nothing.</para>
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_StillRegistersTheMovedStrays()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<MissionPlanQueue>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<ActorCapabilityState>()));
        }

        /// <summary>
        /// ⭐⭐ The structural half — the BRAIN's registry no longer owns them.
        /// ⚠ <c>PreviousCapabilities</c> deliberately STAYS there: its only readers are
        /// <c>CognitiveInterruptSystem</c> (Brain) and the Stride animation reactor, so it is ABSENT for
        /// SimHost and must not move with its sibling (design §3.9a).
        /// </summary>
        [Fact]
        public void CognitiveComponentRegistry_NoLongerRegistersTheMovedStrays()
        {
            using var world = new EntityRepository();
            CognitiveComponentRegistry.RegisterAll(world);

            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<MissionPlanQueue>());
            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<ActorCapabilityState>());

            // ⭐ The sibling that must NOT have moved, and the anti-vacuity check in one.
            Assert.Null(Record.Exception(() => world.GetComponentTable<PreviousCapabilities>()));
        }

        // ── CE-259bf slice 3a: the two CROSS-ROLE sets get their own homes ────────────

        [Fact]
        public void EmbarkationComponentRegistry_RegistersTheEmbarkationState()
        {
            using var world = new EntityRepository();
            EmbarkationComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<PassengerBuffer>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<IsEmbarkedTag>()));
        }

        [Fact]
        public void BehaviorDiagnosticsComponentRegistry_RegistersDebugState()
        {
            using var world = new EntityRepository();
            BehaviorDiagnosticsComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Toolkit.Behavior.Diagnostics.DebugState>()));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The behaviour-preserving claim for slice 3a, on the HOST.</b>
        ///
        /// <para>⛔⛔ <c>DebugState</c> is the one with a live SimHost writer — its own <c>ToggleAiTrace</c>
        /// action (<c>SimHostApp.cs:443</c>). ⚠ And the embark/disembark COMMANDS moved with their
        /// components deliberately: <c>EnforceExplicitEventRegistration</c> turns an unregistered publish
        /// into a THROW, so a half-move would convert a registry omission into a runtime crash.</para>
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_StillRegistersTheCrossRoleSets()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<PassengerBuffer>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<IsEmbarkedTag>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Toolkit.Behavior.Diagnostics.DebugState>()));
        }

        /// <summary>
        /// ⚠ The ring buffers must NOT have moved with <c>DebugState</c>: they are written only by
        /// <c>TraceBufferLifecycleSystem</c> (Brain) and their SimHost readers are extract-only
        /// translators gated on <c>BehaviorState</c> (design §3.9a).
        /// </summary>
        [Fact]
        public void CognitiveComponentRegistry_KeepsTheTraceRingBuffers_ButNotDebugState()
        {
            using var world = new EntityRepository();
            CognitiveComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Toolkit.Behavior.Diagnostics.BTreeTraceWorkingMemory1024>()));
            Assert.ThrowsAny<System.Exception>(
                () => world.GetComponentTable<Fdp.Toolkit.Behavior.Diagnostics.DebugState>());
        }

        // ── CE-259bf slice 3b: SimHost no longer registers the BRAIN ─────────────────
        // 🔒 User ruling 2026-09-12: "Simhost has no ai(brain). So it does not need [them]."

        /// <summary>
        /// ⭐⭐⭐ <b>THE POINT OF THE WHOLE CHAIN — a node with no brain no longer materialises one.</b>
        ///
        /// <para>📄 §3.9a/§6h. SimHost runs NO cognitive system, yet registered the entire brain tier, so
        /// every spawn carried components nothing there would ever tick. ⛔ If this reddens, the brain
        /// tier has come back to a Muscle node — which is the defect this design opens on.</para>
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_DoesNotRegisterTheBrainTier()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<BehaviorState>());
            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<BrainBTreeState>());
            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<BrainBlackboard>());
            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<Blackboard1024>());
            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<BrainHsm128>());
            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<BrainHsm64>());
            Assert.ThrowsAny<System.Exception>(() => world.GetComponentTable<LocomotionChannel>());
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The other half, and the one that makes the drop SAFE rather than merely smaller.</b>
        ///
        /// <para>⛔⛔ Every component SimHost genuinely uses had to be moved to a registry SimHost calls
        /// BEFORE the cognitive call could go. ⚠ This rail is what would have caught doing it in the
        /// other order — and the failure would have been SILENT, since a missing component makes a
        /// translator skip rather than throw.</para>
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_StillRegistersEverythingItActuallyUses()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationIntent>()));     // MuscleRole
            Assert.Null(Record.Exception(() => world.GetComponentTable<MissionPlanQueue>()));     // Mission
            Assert.Null(Record.Exception(() => world.GetComponentTable<ActorCapabilityState>())); // Combat
            Assert.Null(Record.Exception(() => world.GetComponentTable<PassengerBuffer>()));      // Embarkation
            Assert.Null(Record.Exception(() => world.GetComponentTable<IsEmbarkedTag>()));        // Embarkation
            Assert.Null(Record.Exception(() => world.GetComponentTable<EqsSensor>()));            // Perception
            Assert.Null(Record.Exception(
                () => world.GetComponentTable<Fdp.Toolkit.Behavior.Diagnostics.DebugState>()));   // Diagnostics
        }

        /// <summary>
        /// ⭐⭐ <b>CGF is UNAFFECTED — it still gets the whole brain tier.</b>
        /// ⛔ The drop must be per-HOST, not a deletion from the shared registry. ⚠ Without this, emptying
        /// <c>CognitiveComponentRegistry</c> would also satisfy the rail above.
        /// </summary>
        [Fact]
        public void TheBrainTierStillExists_ForHostsThatRunABrain()
        {
            using var world = new EntityRepository();
            CognitiveComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<BehaviorState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<BrainBTreeState>()));
            Assert.Null(Record.Exception(() => world.GetComponentTable<LocomotionChannel>()));
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

        /// <summary>
        /// ⚠⚠ <b>RENAMED AND RE-AIMED <c>2026-09-12</c> (<c>CE-259bf</c> slice 3b) — the CLAIM is kept,
        /// the SAMPLE was corrected.</b>
        ///
        /// <para>This rail guards <b>delegation</b>: composing the sub-registries must still yield the set
        /// SimHost needs. ⛔ It happened to sample <c>BehaviorState</c>, and 🔒 the user ruled
        /// <i>"Simhost has no ai(brain). So it does not need [them]"</i> ⇒ that sample now asserts the
        /// OPPOSITE of the intended contract. ⭐ It is replaced by <c>MissionPlanQueue</c> — a component
        /// that reaches SimHost through a DIFFERENT sub-registry than it used to, so the delegation claim
        /// is still exercised across a boundary that actually moved.</para>
        ///
        /// <para>⛔ The brain half of the old assertion did not vanish — it moved to
        /// <c>SimHostComponentRegistry_DoesNotRegisterTheBrainTier</c>, inverted, above.</para>
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_RegisterAll_StillProvidesTheDelegatedSet()
        {
            using var world = new EntityRepository();
            // The refactored SimHostComponentRegistry delegates to sub-registries.
            // Verify the full set of components remains accessible.
            SimHostComponentRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => world.GetComponentTable<MissionPlanQueue>()));
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


        // ═══ P3 — THE ROLE TABLE AND THE HAND-AUTHORED REGISTRY MUST NOT DRIFT ═════════════════
        //  📄 docs/DESIGN_Role_Affinity_Ownership.md §3.9 (REGISTER = owned ∪ read), §6h, §6j.
        //
        //  🔴 WHY THESE EXIST. P3's narrowing is real — SimHostComponentRegistry deliberately does NOT
        //    call CognitiveComponentRegistry, and calls MuscleRoleComponentRegistry instead. ⛔ But WHAT
        //    it registers is HAND-AUTHORED, while HrotRoleComponentSets.BrainOnlyComponents states the
        //    same fact declaratively. That is TWO PRODUCERS OF ONE FACT, and until the role tables become
        //    positive enumerations nothing can derive one from the other:
        //    📐 the tables are COMPLEMENTS (owned = ALL − birthCritical − brainOnly ≈ 496 of 512 bits), so
        //    IRoleAffinityPolicy.RegisterComponentSet is ~498 bits and CANNOT drive registration. It is
        //    computed, correct, and read by NOTHING in production — measured 2026-09-13.
        //  ⇒ ⭐ these rails are the only thing keeping the two in step. They pass TODAY; that is the point.

        /// <summary>⭐ Registration is checked by TYPE because that is what the repository keys on;
        /// the role table speaks in component IDS. This bridges the two the same way the policy does.</summary>
        private static bool IsRegistered(EntityRepository world, int componentId)
        {
            var type = ComponentTypeRegistry.GetType(componentId);
            return type != null && world.TryGetTable(type, out _);
        }

        /// <summary>
        /// 🔴🔴 <b>A Muscle node must REGISTER every component its role READS.</b>
        /// 🔒 User, <c>2026-09-12</c>: <i>"intents are brain owned components that must be replicated to
        /// muscle so musle can read and act on them. so muscle cant simply stop registwring them because
        /// they are brain ones."</i>
        ///
        /// <para>⛔ This is the failure the narrowing could cause and the one that would hurt most: a
        /// SimHost that stopped registering <c>NavigationIntent</c>/<c>MissionPlanQueue</c> would
        /// <b>stop receiving its own orders</b>, silently — registration is the only gate on
        /// materialisation (<c>BehaviorTkbTranslator.cs:52</c>), so the component would simply never
        /// appear and every intent would land on nothing.</para>
        /// </summary>
        [Fact]
        public void SimHostRegistersEveryComponentItsRoleREADS()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            var read = HrotRoleComponentSets.MuscleReadComponents;
            for (int id = 0; id < FdpConfig.MAX_COMPONENT_TYPES; id++)
            {
                if (!read.IsSet(id)) continue;
                Assert.True(IsRegistered(world, id),
                    $"SimHost must REGISTER read-only component id {id} " +
                    $"({ComponentTypeRegistry.GetType(id)?.Name}) — it is Brain-OWNED but replicated IN " +
                    "and consumed here. Without registration the node stops receiving its own orders.");
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>…and must register NONE of the brain-only ones.</b> That is the narrowing itself,
        /// asserted against the declarative table rather than against the registry's own source.
        ///
        /// <para>⚠ <c>BrainOnlyComponents</c> deliberately INCLUDES the read pair (the table ORs
        /// <c>muscleRead</c> into it so those ids stay out of the Muscle OWNED set), so the read set is
        /// subtracted here. ⛔ Without that subtraction this rail would contradict the one above.</para>
        /// </summary>
        [Fact]
        public void SimHostRegistersNoBrainOnlyComponent()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            var brainOnly = HrotRoleComponentSets.BrainOnlyComponents;
            var read      = HrotRoleComponentSets.MuscleReadComponents;

            for (int id = 0; id < FdpConfig.MAX_COMPONENT_TYPES; id++)
            {
                if (!brainOnly.IsSet(id) || read.IsSet(id)) continue;
                Assert.False(IsRegistered(world, id),
                    $"SimHost must NOT register brain-only component id {id} " +
                    $"({ComponentTypeRegistry.GetType(id)?.Name}). P3's narrowing says a Muscle node does " +
                    "not materialise the brain tier; registration is the only gate on materialisation.");
            }
        }

        /// <summary>
        /// ⛔⛔ <b>ANTI-VACUITY.</b> Both rails above iterate a mask; if either mask were ever empty they
        /// would pass over nothing and keep passing forever while the narrowing rotted away.
        /// 📌 The same trap <c>ComponentAttributeSetsTests.NeitherSetIsEmpty</c> exists for.
        /// </summary>
        [Fact]
        public void TheRoleMasksTheseRailsIterateAreNotEmpty()
        {
            Assert.False(HrotRoleComponentSets.BrainOnlyComponents.IsEmpty());
            Assert.False(HrotRoleComponentSets.MuscleReadComponents.IsEmpty());
        }
    }
}

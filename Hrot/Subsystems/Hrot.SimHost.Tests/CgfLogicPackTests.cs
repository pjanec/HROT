using System;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Blueprints.Systems;
using System.Linq;
using Fdp.Toolkit.CarKinem.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Hrot.CGF;
using Hrot.CGF.Systems;
using Hrot.Common.Systems;
using Hrot.Core.Network;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CgfLogicPack"/> (PACK2-P001) and TASK-C003 wiring.
    /// </summary>
    public class CgfLogicPackTests
    {
        private static EntityRepository CreateEmptyWorld()
        {
            var world = new EntityRepository();

            // Behavior toolkit components
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.InteractionChannel>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.ActorCapabilityState>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBTreeState>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainHsm64>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainHsm128>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PreviousCapabilities>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();

            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            return world;
        }

        // -- Helpers for C003 CreateEntityRequestSystem tests --

        private const long  C003ValidTkbType = 42L;
        private const ulong C003ValidDisType = 0x0100_0000_0000_0001UL;
        private const int   C003LocalNodeId  = 9;

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("TestVehicle", C003ValidTkbType));
            return db;
        }

        private static EntityRepository CreateWorldForRequestSystem()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.ConstructionOrder>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            return repo;
        }

        private static EntityCreationRequest MakeValidRequest() =>
            new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = C003LocalNodeId,
                TkbType            = C003ValidTkbType,
                DisType            = C003ValidDisType,
            };

        // -- Tests (PACK2-P001 existing, updated for new scenarioSource param) --

        /// <summary>
        /// All three sub-module system sets register without error and run on an
        /// empty world without throwing.
        /// </summary>
        // A (Stale Test TH-3): DebugStatePatchSystem removed from CognitiveRuntimeModule.InputSystems;
        // InputSystems count dropped from 3 to 2; total from 21 to 20.
        [Fact]
        public void CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException()
        {
            using var world   = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack    = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            var view = (ISimulationView)world;
            var ex = Record.Exception(() =>
            {
                foreach (var s in pack.InputSystems)      s.Execute(view, 0.016f);
                foreach (var s in pack.SimulationSystems) s.Execute(view, 0.016f);
            });
            Assert.Null(ex);

            // InputSystems: MissionControlExecutionSystem (1), BehaviorIngressSystem (1) = 2
            // (DebugStatePatchSystem removed from CognitiveRuntimeModule — TH-3/A)
            // SimulationSystems: 18 (unchanged)
            Assert.Equal(2,  pack.InputSystems.Count);
            // ⭐ SimulationSystems: 19. ⚠ Was 18 and RED since `2026-08-19` — Batch 94b added
            //   `BehaviorFrameSystem` to `CognitiveRuntimeModule` (`:57`), which flows in here.
            //   📌 A hard-coded count is a tripwire for exactly this, and it fired; nobody read it.
            // ⭐⭐ CE-221 — 2 fewer: UnitHierarchySystem and EqsResultUpdateSystem left this pack.
            //    They are cross-role infrastructure (no role selects them; every carrier appended them
            //    at the tail of Simulation), and carrying them in BOTH the Brain and Muscle packs made
            //    every fusing node register each twice. They now come from the infrastructure
            //    capabilities, declared once per plan, so the NODE still runs exactly one of each.
            // ⭐⭐ A4/O0 (2026-09-20) — 1 MORE: BlueprintTickSystem. CgfLogicPack now splices it
            //    before its own action dispatchers, so CGF ticks blueprint Instances too and not
            //    only the Editor (CE-161's defect shape, one level up: the tier COMPONENTS moved
            //    to a shared path, the SCHEDULING stayed in Hrot.Blueprints.Editor).
            Assert.Equal(18, pack.SimulationSystems.Count);

            // ⛔ Assert the REMOVAL too — a count alone is the kind of thing a later session
            //    re-baselines without reading why it moved.
            Assert.DoesNotContain(pack.SimulationSystems, x => x is Hrot.Common.Systems.UnitHierarchySystem);
            Assert.DoesNotContain(pack.SimulationSystems, x => x is Hrot.SimHost.Systems.EqsResultUpdateSystem);
        }

        /// <summary>
        /// Verifies that systems belonging to each of the three sub-modules are
        /// in the simulation group.
        /// </summary>
        [Fact]
        public void CgfLogicPack_ContainsSystemsFromAllThreeSubModules()
        {
            using var world      = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack     = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            // MissionControlModule systems in InputSystems + SimulationSystems
            Assert.Contains(pack.InputSystems,      s => s is BehaviorIngressSystem);
            Assert.Contains(pack.SimulationSystems, s => s is MissionDirectorSystem);

            // CognitiveRuntimeModule systems
            Assert.Contains(pack.SimulationSystems, s => s is ChannelArbitrationSystem);
            Assert.Contains(pack.SimulationSystems, s => s is BTreeTickSystem);

            // ActionDispatchModule systems
            Assert.Contains(pack.SimulationSystems, s => s is LocomotionDispatcherSystem);
            Assert.Contains(pack.SimulationSystems, s => s is WeaponDispatcherSystem);
        }

        /// <summary>
        /// Verifies the module Name property.
        /// </summary>
        [Fact]
        public void CgfLogicPack_Name_IsCgfLogicPack()
        {
            var pack = new CgfLogicPack(
                new BehaviorRegistry(),
                new NetworkEntityMap(),
                new ScenarioEntityCreationRequestSource(),
                new TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            Assert.Equal("CgfLogicPack", pack.Name);
        }

        // -- Tests (TASK-C003) --

        /// <summary>
        /// C003 success condition 4: CgfLogicPack rejects null scenarioSource.
        /// </summary>
        [Fact]
        public void CgfLogicPack_NullScenarioSource_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new CgfLogicPack(
                    new BehaviorRegistry(),
                    new NetworkEntityMap(),
                    scenarioSource: null!,
                    mapperRegistry: new TacticalIntentMapperRegistry(), blueprintRegistry: new Fdp.Toolkit.Blueprints.BlueprintRegistry()));

            Assert.Equal("scenarioSource", ex.ParamName);
        }

        /// <summary>
        /// C003 success condition 1: requests from the NED stub source reach
        /// SpawnEntityCommand when CreateEntityRequestSystem uses the composite.
        /// </summary>
        [Fact]
        public void C003_NedRequestsProcessed_ViaCompositeSource()
        {
            var repo    = CreateWorldForRequestSystem();
            var tkb     = CreateTkb();
            var nedStub = new StubRequestSource();
            var scenarioSource = new ScenarioEntityCreationRequestSource();

            var composite = new CompositeEntityCreationRequestSource(
                new IEntityCreationRequestSource[] { nedStub, scenarioSource });

            var system = new CreateEntityRequestSystem(
                composite, new StubAckSink(), tkb,
                new StubIdAllocator(startId: 100), C003LocalNodeId);

            nedStub.Enqueue(MakeValidRequest());

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.NotEmpty(commands);
        }

        /// <summary>
        /// C003 success condition 2: requests from the scenario source reach
        /// SpawnEntityCommand when CreateEntityRequestSystem uses the composite.
        /// </summary>
        [Fact]
        public void C003_ScenarioRequestsProcessed_ViaCompositeSource()
        {
            var repo    = CreateWorldForRequestSystem();
            var tkb     = CreateTkb();
            var nedStub = new StubRequestSource();
            var scenarioSource = new ScenarioEntityCreationRequestSource();

            var composite = new CompositeEntityCreationRequestSource(
                new IEntityCreationRequestSource[] { nedStub, scenarioSource });

            var system = new CreateEntityRequestSystem(
                composite, new StubAckSink(), tkb,
                new StubIdAllocator(startId: 200), C003LocalNodeId);

            scenarioSource.Enqueue(MakeValidRequest());

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.NotEmpty(commands);
        }

        /// <summary>
        /// C003 success condition 3: requests from BOTH sources reach SpawnEntityCommand
        /// in the same tick when CreateEntityRequestSystem uses the composite.
        /// </summary>
        [Fact]
        public void C003_BothSourcesProcessed_SameTick()
        {
            var repo    = CreateWorldForRequestSystem();
            var tkb     = CreateTkb();
            var nedStub = new StubRequestSource();
            var scenarioSource = new ScenarioEntityCreationRequestSource();

            var composite = new CompositeEntityCreationRequestSource(
                new IEntityCreationRequestSource[] { nedStub, scenarioSource });

            var system = new CreateEntityRequestSystem(
                composite, new StubAckSink(), tkb,
                new StubIdAllocator(startId: 300), C003LocalNodeId);

            nedStub.Enqueue(MakeValidRequest());
            scenarioSource.Enqueue(MakeValidRequest());

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Equal(2, commands.Count);
        }

        // -- S306: Two-group overload routes systems correctly --

        /// <summary>
        /// S306-SC1/SC2/SC3: The two-group overload places <see cref="MissionControlExecutionSystem"/>
        /// and <see cref="BehaviorIngressSystem"/> in the Input group, and all remaining
        /// systems in the Simulation group.
        /// </summary>
        // A (Stale Test TH-3): DebugStatePatchSystem removed from CognitiveRuntimeModule.InputSystems;
        // InputSystems count corrected from 3 to 2.
        [Fact]
        public void CgfLogicPack_TwoGroupOverload_RoutesSystemsCorrectly()
        {
            using var world      = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack       = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            // SC1: MissionControlExecutionSystem is in InputSystems.
            Assert.Contains(pack.InputSystems, s => s is MissionControlExecutionSystem);
            // SC2: BehaviorIngressSystem is in InputSystems.
            Assert.Contains(pack.InputSystems, s => s is BehaviorIngressSystem);
            // SC3: MissionDirectorSystem is in SimulationSystems.
            Assert.Contains(pack.SimulationSystems, s => s is MissionDirectorSystem);
            // MissionAdapterSystem stays in SimulationSystems.
            Assert.Contains(pack.SimulationSystems, s => s is MissionAdapterSystem);

            // InputSystems: MissionControlExecutionSystem + BehaviorIngressSystem = 2
            // (DebugStatePatchSystem removed from CognitiveRuntimeModule — TH-3/A)
            Assert.Equal(2,  pack.InputSystems.Count);
            // ⭐ SimulationSystems: 19. ⚠ Was 18 and RED since `2026-08-19` — Batch 94b added
            //   `BehaviorFrameSystem` to `CognitiveRuntimeModule` (`:57`), which flows in here.
            //   📌 A hard-coded count is a tripwire for exactly this, and it fired; nobody read it.
            // ⭐⭐ CE-221 — 2 fewer: UnitHierarchySystem and EqsResultUpdateSystem left this pack.
            //    They are cross-role infrastructure (no role selects them; every carrier appended them
            //    at the tail of Simulation), and carrying them in BOTH the Brain and Muscle packs made
            //    every fusing node register each twice. They now come from the infrastructure
            //    capabilities, declared once per plan, so the NODE still runs exactly one of each.
            // ⭐⭐ A4/O0 (2026-09-20) — 1 MORE: BlueprintTickSystem. CgfLogicPack now splices it
            //    before its own action dispatchers, so CGF ticks blueprint Instances too and not
            //    only the Editor (CE-161's defect shape, one level up: the tier COMPONENTS moved
            //    to a shared path, the SCHEDULING stayed in Hrot.Blueprints.Editor).
            Assert.Equal(18, pack.SimulationSystems.Count);
        }

        /// <summary>
        /// S306-SC4: The existing single-group overload still adds all systems to the same
        /// group (no regression).
        /// </summary>
        // A (Stale Test TH-3): total system count corrected from 21 to 20
        // (DebugStatePatchSystem removed from CognitiveRuntimeModule.InputSystems).
        [Fact]
        public void CgfLogicPack_SingleGroupOverload_StillAddsAllSystemsToOneGroup()
        {
            using var world      = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack     = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            // Total systems across both phases equals 21 (2 input + 19 sim) — see the note above.
            // ⭐⭐ CE-221 — 2 fewer: UnitHierarchySystem and EqsResultUpdateSystem left this pack.
            //    They are cross-role infrastructure (no role selects them; every carrier appended them
            //    at the tail of Simulation), and carrying them in BOTH the Brain and Muscle packs made
            //    every fusing node register each twice. They now come from the infrastructure
            //    capabilities, declared once per plan, so the NODE still runs exactly one of each.
            // ⭐⭐ A4/O0 (2026-09-20) — 1 MORE: BlueprintTickSystem. CgfLogicPack now splices it
            //    before its own action dispatchers, so CGF ticks blueprint Instances too and not
            //    only the Editor (CE-161's defect shape, one level up: the tier COMPONENTS moved
            //    to a shared path, the SCHEDULING stayed in Hrot.Blueprints.Editor).
            Assert.Equal(20, pack.InputSystems.Count + pack.SimulationSystems.Count);
        }

        // ── CE-200: CGF composes from the capability seam (B4b step 2, host (c)) ──────
        //
        // ⭐⭐ These pin the SEQUENCE, because resolution order is registration order is EXECUTION
        // order (ModuleHostKernel.RegisterModule appends to a plain list the frame loop walks). A
        // switchover from a hand-written block is behaviour-preserving only if the order matches.

        private static CgfLogicPack NewPack()
            => new CgfLogicPack(
                new BehaviorRegistry(),
                new NetworkEntityMap(),
                new ScenarioEntityCreationRequestSource(),
                new TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());

        private static System.Collections.Generic.IReadOnlyList<Hrot.Common.Infrastructure.INodeCapability>
            ResolveBrain(CgfLogicPack pack)
            => new Hrot.Common.Infrastructure.NodeCompositionPlan()
                .Capability(CgfSubsystem.DefaultRole, new CgfCapabilities.Brain(pack))
                .Resolve(CgfSubsystem.DefaultRole);

        [Fact]
        public void BrainCapability_ProvidesModules_InTheOrderTheRootRegisteredThemByHand()
        {
            var pack = NewPack();
            var modules = new System.Collections.Generic.List<IEcsModule>();
            foreach (var capability in ResolveBrain(pack))
                modules.AddRange(capability.ProvideModules());

            // Verbatim from the block this replaced: diagnostics first, then the pack.
            // ⭐⭐ A4/O0 (2026-09-20) — a THIRD module now follows: the BeforeSync blueprint
            //    maintenance system, carried by a SingleSystemModule. ⛔ It cannot ride the pack's
            //    SimulationSystems the way the tick does, and registering it per composition root is
            //    the per-host chance to forget that CE-161 was made of.
            Assert.Equal(3, modules.Count);
            Assert.IsType<BehaviorDiagnosticsModule>(modules[0]);
            Assert.Same(pack, modules[1]);
        }

        [Fact]
        public void BrainCapability_HandsOutBothPhaseLists_AndContributesNoPostSimulationSystem()
        {
            var pack    = NewPack();
            var input   = new System.Collections.Generic.List<IEcsModuleSystem>();
            var sim     = new System.Collections.Generic.List<IEcsModuleSystem>();
            var postSim = new System.Collections.Generic.List<IEcsModuleSystem>();

            foreach (var capability in ResolveBrain(pack))
                capability.PopulateSystems(null!, input, sim, postSim);

            Assert.Equal(pack.InputSystems,      input);
            Assert.Equal(pack.SimulationSystems, sim);

            // ⛔ CGF builds NO post-simulation group. If this ever stops being empty the root throws
            //    rather than dropping the systems — this rail says the root's guard is not dead code.
            Assert.Empty(postSim);
        }

        [Fact]
        public void BrainCapability_DeclaresTheBrainKey_AndNeedsNoSharedResource()
        {
            var capability = Assert.Single(ResolveBrain(NewPack()));

            Assert.Equal(Hrot.Common.Infrastructure.CapabilityKeys.Brain, capability.Key);

            // ⭐ Measured, not assumed: the pack takes a behaviour registry, an entity map, a scenario
            //   source and a mapper registry — no pool, no grid. The trajectory pool belongs to
            //   MuscleGround. CE-199's cross-check makes a future Need loud rather than silent.
            Assert.Empty(capability.Needs);
        }

        [Fact]
        public void ARoleWithoutBrain_ResolvesNoCapability()
        {
            // The selection must actually SELECT — a plan returning its declarations regardless of role
            // would make the flags decorative, which is the defect CE-197 measured on SimHost.
            var resolved = new Hrot.Common.Infrastructure.NodeCompositionPlan()
                .Capability(CgfSubsystem.DefaultRole, new CgfCapabilities.Brain(NewPack()))
                .Resolve(NodeRole.Map2D);

            Assert.Empty(resolved);
        }

        // ══ A4 / O0 — the blueprint runtime reaches EVERY Brain host, not only the Editor ═════════
        //
        // 📐 CE-161 (2026-09-03) measured a --mode all cluster aborting on CGF with "Component
        //    BlueprintBlackboard1024 is not registered", because the only code registering the tiers
        //    lived in Hrot.Blueprints.Editor. Its fix moved the COMPONENTS to a shared path. The
        //    SCHEDULING stayed behind, so no host but the Editor ever ticked a blueprint Instance.
        // ⇒ these rails pin the fix at the seam, so a future composition change cannot quietly undo it.

        /// <summary>
        /// ⭐⭐ A4-R1 — the pack carries EXACTLY ONE <see cref="BlueprintTickSystem"/>.
        ///
        /// <para>⛔ "Exactly one" is the load-bearing half. The Editor used to splice its own instance at
        /// the composition root; with the pack splicing one too, a root that kept its splice would put
        /// TWO in a single group — and <c>DistinctByType</c> runs BEFORE the root splice, so it cannot
        /// catch it. Every slot would then tick twice per frame.</para>
        /// </summary>
        [Fact]
        public void A4_R1_ThePack_CarriesExactlyOneBlueprintTickSystem()
        {
            var pack = NewPack();

            Assert.Single(pack.SimulationSystems, s => s is BlueprintTickSystem);
            Assert.DoesNotContain(pack.InputSystems, s => s is BlueprintTickSystem);
        }

        /// <summary>
        /// 🔴 A4-R2 — the tick sits BEFORE the action dispatchers it declares <c>[UpdateBefore]</c> on.
        ///
        /// <para>Module-group execution order is ARRAY POSITION — the kernel does not re-apply ordering
        /// attributes inside a module's system list. ⛔ An APPENDED tick runs after the dispatchers, so
        /// an intent written by a blueprint is dispatched a tick late: the <c>Q#16-B</c> "intent is read
        /// the same tick" contract, silently downgraded. That is exactly what both real compositions did
        /// before <c>FC-1·G2</c>, and moving the splice into the pack is a chance to reintroduce it.</para>
        /// </summary>
        [Fact]
        public void A4_R2_TheTick_IsSplicedBeforeTheActionDispatchers()
        {
            var pack = NewPack();
            var sim  = pack.SimulationSystems;

            int tickAt = -1, firstDispatcherAt = -1;
            for (int i = 0; i < sim.Count; i++)
            {
                if (tickAt < 0 && sim[i] is BlueprintTickSystem) tickAt = i;
                if (firstDispatcherAt < 0 &&
                    (sim[i] is LocomotionDispatcherSystem || sim[i] is WeaponDispatcherSystem))
                    firstDispatcherAt = i;
            }

            // anti-vacuity: an empty list, or one with no dispatcher, would make the ordering assert
            // below trivially true — and "appended at the end" is the degenerate case it must not pass.
            Assert.True(tickAt >= 0, "the pack must carry a BlueprintTickSystem at all");
            Assert.True(firstDispatcherAt >= 0,
                "the pack must carry an action dispatcher, or this rail asserts nothing");

            Assert.True(tickAt < firstDispatcherAt,
                $"BlueprintTickSystem is at {tickAt} but the first dispatcher is at {firstDispatcherAt} — " +
                "an appended tick dispatches blueprint intent one tick late (Q#16-B).");
        }

        /// <summary>
        /// ⭐ A4-R3 — the Brain capability carries the BeforeSync maintenance system into the kernel,
        /// and it is the SAME instance the pack built.
        ///
        /// <para>🔴 Without it a host can tick Instances but never PROMOTE a tier — and since A3 the
        /// promotion path is also what carries the <c>OccurrenceKind</c> nibble array across a tier
        /// upgrade (<c>H1</c>).</para>
        /// </summary>
        [Fact]
        public void A4_R3_TheBrainCapability_CarriesTheMaintenanceSystem()
        {
            var pack = NewPack();
            var modules = new System.Collections.Generic.List<IEcsModule>();
            foreach (var capability in ResolveBrain(pack))
                modules.AddRange(capability.ProvideModules());

            var carrier = Assert.Single(
                modules.OfType<Fdp.ModuleHost.Scheduling.SingleSystemModule>());

            var registry = new CapturingSystemRegistry();
            carrier.RegisterSystems(registry);

            Assert.Same(pack.MaintenanceSystem, Assert.Single(registry.Systems));
        }

        /// <summary>Minimal <see cref="ISystemRegistry"/> that records what a module registers.</summary>
        private sealed class CapturingSystemRegistry : ISystemRegistry
        {
            public System.Collections.Generic.List<IEcsModuleSystem> Systems { get; } = new();

            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system);

            public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
            {
                Systems.Add(system);
                return system;
            }
        }
    }
}

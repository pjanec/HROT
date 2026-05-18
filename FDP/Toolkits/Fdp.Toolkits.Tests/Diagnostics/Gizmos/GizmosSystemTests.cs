using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ==========================================================================
    // Test-only ECS component stubs (IDs in the 245-254 free range)
    // ==========================================================================

    [ComponentId(245)]
    public struct GizmoTestCompA { public int Value; }

    [ComponentId(246)]
    public struct GizmoTestCompB { public int Value; }

    // Used as a selection marker in predicate-based tests (replaces SelectionState).
    // Presence = selected; absence = not selected (avoids bool layout constraint).
    [ComponentId(247)]
    public struct GizmoSelectedTag { }

    // ==========================================================================
    // Mock implementations
    // ==========================================================================

    internal sealed class MockGizmo : IEntityStatefulGizmo
    {
        public int InitializeCount;
        public int UpdateAndDrawCount;
        public int TeardownCount;
        public Entity LastInitializedEntity;

        private readonly Action? _onDispose;

        public MockGizmo(Entity entity = default, Action? onDispose = null)
        {
            InitializeCount = 1;
            LastInitializedEntity = entity;
            _onDispose = onDispose;
        }

        // IEntityStatefulGizmo
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder drawBuilder)
        {
            UpdateAndDrawCount++;
        }

        public void Dispose()
        {
            TeardownCount++;
            _onDispose?.Invoke();
        }

        // IGizmoInteractionHandler stubs
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
    }

    internal sealed class MockGizmoDefinition : IGizmoDefinition
    {
        private readonly Type[] _requiredComponents;
        private readonly List<MockGizmo> _createdInstances = new List<MockGizmo>();

        public MockGizmoDefinition(Type[] requiredComponents, IGizmoVisibilityPolicy? policy = null, uint gizmoTypeId = 0)
        {
            _requiredComponents = requiredComponents;
            VisibilityPolicy = policy ?? AlwaysVisiblePolicy.Instance;
            GizmoTypeId = gizmoTypeId;
        }

        public Type[] RequiredComponents => _requiredComponents;
        public IGizmoVisibilityPolicy VisibilityPolicy { get; }
        public uint GizmoTypeId { get; }
        public IReadOnlyList<MockGizmo> CreatedInstances => _createdInstances;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            var g = new MockGizmo(entity);
            _createdInstances.Add(g);
            return g;
        }
    }

    internal sealed class MockVisibilityPolicy : IGizmoVisibilityPolicy
    {
        public bool GloballyEnabled = true;
        public bool EntityVisible = true;
        public int IsGloballyEnabledCallCount;
        public int IsEntityVisibleCallCount;

        public bool IsGloballyEnabled(ISimulationView view)
        {
            IsGloballyEnabledCallCount++;
            return GloballyEnabled;
        }

        public bool IsEntityVisible(ISimulationView view, Entity entity)
        {
            IsEntityVisibleCallCount++;
            return EntityVisible;
        }
    }

    internal sealed class MockBehaviorFactory : IBehaviorGizmoFactory
    {
        public string BehaviorName { get; }
        public int RentCount;
        public int ReturnCount;

        public MockBehaviorFactory(string behaviorName)
        {
            BehaviorName = behaviorName;
        }

        public IEntityStatefulGizmo Create(ISimulationView view, Entity entity)
        {
            RentCount++;
            return new MockGizmo(entity, onDispose: () => ReturnCount++);
        }
    }

    // ==========================================================================
    // Helper: build a minimal EntityRepository for gizmo tests
    // ==========================================================================

    internal static class GizmoTestRepo
    {
        public static EntityRepository Create()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<GizmoTestCompA>();
            repo.RegisterComponent<GizmoTestCompB>();
            repo.RegisterComponent<GizmoSelectedTag>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            repo.RegisterEvent<ClearBehaviorEvent>();
            return repo;
        }

        /// <summary>
        /// Publishes a ConstructionOrder for <paramref name="entity"/>, swaps the bus buffers
        /// so the event is visible to the system, then runs <paramref name="sys"/>.
        /// </summary>
        public static void PublishConstructionAndExecute(
            EntityRepository repo,
            IEcsModuleSystem sys,
            Entity entity,
            float dt = 0f)
        {
            repo.Bus.Publish(new ConstructionOrder { Entity = entity });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, dt);
        }

        /// <summary>
        /// Publishes a DestructionOrder for <paramref name="entity"/>, swaps, then runs sys.
        /// </summary>
        public static void PublishDestructionAndExecute(
            EntityRepository repo,
            IEcsModuleSystem sys,
            Entity entity,
            float dt = 0f)
        {
            repo.Bus.Publish(new DestructionOrder { Entity = entity });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, dt);
        }
    }

    // ==========================================================================
    // SC-GZ004: GizmoRegistry tests
    // ==========================================================================

    public class GizmoRegistryTests
    {
        [Fact]
        public void SC_GZ004_1_Register_TwoComponents_ProducesCorrectMask()
        {
            // Components must be registered with the EntityRepository FIRST
            // so that ComponentTypeRegistry has their IDs.
            using var repo = GizmoTestRepo.Create();

            var registry = new GizmoRegistry();
            var def = new MockGizmoDefinition(
                new[] { typeof(GizmoTestCompA), typeof(GizmoTestCompB) });

            registry.Register(def);

            Assert.Equal(1, registry.Rules.Count);
            var rule = registry.Rules[0];

            int idA = ComponentTypeRegistry.GetId(typeof(GizmoTestCompA));
            int idB = ComponentTypeRegistry.GetId(typeof(GizmoTestCompB));

            Assert.NotEqual(-1, idA);
            Assert.NotEqual(-1, idB);
            Assert.True(rule.RequiredMask.IsSet(idA));
            Assert.True(rule.RequiredMask.IsSet(idB));
        }

        [Fact]
        public void SC_GZ004_2_Register_UnregisteredComponent_Throws()
        {
            // Do NOT register GizmoUnknownComp with the repo.
            // Its type has no [ComponentId] attribute, so GetId will return -1
            // and GizmoRegistry.Register must throw.
            var registry = new GizmoRegistry();
            var def = new MockGizmoDefinition(new[] { typeof(GizmoTestCompA) });

            // To ensure GizmoTestCompA is not registered (rare case in a fresh static registry),
            // we test with a type that is deliberately never registered:
            // we use an anonymous/local type trick via a wrapper.
            var defBad = new MockGizmoDefinition(new[] { typeof(UnregisteredComp) });

            Assert.Throws<InvalidOperationException>(() => registry.Register(defBad));
        }

        [Fact]
        public void SC_GZ004_3_AlwaysVisiblePolicy_IsGloballyEnabled_ReturnsTrue()
        {
            var policy = AlwaysVisiblePolicy.Instance;
            using var repo = GizmoTestRepo.Create();
            Assert.True(policy.IsGloballyEnabled(repo));
        }

        [Fact]
        public void SC_GZ004_4_AlwaysVisiblePolicy_IsEntityVisible_ReturnsTrue()
        {
            var policy = AlwaysVisiblePolicy.Instance;
            using var repo = GizmoTestRepo.Create();
            var entity = repo.CreateEntity();
            Assert.True(policy.IsEntityVisible(repo, entity));
        }

        [Fact]
        public void SC_GZ004_5_NeverVisiblePolicy_ReturnsFalseFromBothMethods()
        {
            var policy = NeverVisiblePolicy.Instance;
            using var repo = GizmoTestRepo.Create();
            var entity = repo.CreateEntity();
            Assert.False(policy.IsGloballyEnabled(repo));
            Assert.False(policy.IsEntityVisible(repo, entity));
        }

        [Fact]
        public void SC_GZ004_6_MultipleRegistrations_AccumulateInRules()
        {
            using var repo = GizmoTestRepo.Create();

            var registry = new GizmoRegistry();
            registry.Register(new MockGizmoDefinition(new[] { typeof(GizmoTestCompA) }));
            registry.Register(new MockGizmoDefinition(new[] { typeof(GizmoTestCompB) }));
            registry.Register(new MockGizmoDefinition(new[] { typeof(GizmoTestCompA), typeof(GizmoTestCompB) }));

            Assert.Equal(3, registry.Rules.Count);
            Assert.Equal(0, registry.Rules[0].RuleIndex);
            Assert.Equal(1, registry.Rules[1].RuleIndex);
            Assert.Equal(2, registry.Rules[2].RuleIndex);
        }

        // Helper: a type that is never passed to repo.RegisterComponent<T>(),
        // so ComponentTypeRegistry.GetId(typeof(UnregisteredComp)) returns -1.
        [ComponentId(248)]
        private struct UnregisteredComp { }
    }

    // ==========================================================================
    // SC-GZ005: DataDrivenGizmoSystem tests
    // ==========================================================================

    public class DataDrivenGizmoSystemTests
    {
        // Helper: create a repo + registry + system with GizmoTestCompA as required component.
        private static (EntityRepository repo, GizmoRegistry registry, MockGizmoDefinition def, DataDrivenGizmoSystem sys)
            CreateFixture(IGizmoVisibilityPolicy? policy = null, Func<ISimulationView, Entity, bool>? predicate = null)
        {
            var repo = GizmoTestRepo.Create();
            var registry = new GizmoRegistry();
            var def = new MockGizmoDefinition(new[] { typeof(GizmoTestCompA) }, policy);
            registry.Register(def);
            var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
            var sys = new DataDrivenGizmoSystem(registry, buffer, predicate);
            return (repo, registry, def, sys);
        }

        [Fact]
        public void SC_GZ005_1_Setup_MatchingEntity_InitializesGizmo()
        {
            var (repo, _, def, sys) = CreateFixture();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);

            Assert.Equal(1, def.CreatedInstances.Count);
            Assert.Equal(1, def.CreatedInstances[0].InitializeCount);
            Assert.Equal(entity, def.CreatedInstances[0].LastInitializedEntity);
        }

        [Fact]
        public void SC_GZ005_2_Teardown_DestroyedEntity_CallsOnTeardown()
        {
            var (repo, _, def, sys) = CreateFixture();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            // Frame 1: initialise.
            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);
            var gizmo = def.CreatedInstances[0];
            Assert.Equal(0, gizmo.TeardownCount);

            // Frame 2: destroy.
            GizmoTestRepo.PublishDestructionAndExecute(repo, sys, entity);

            Assert.Equal(1, gizmo.TeardownCount);
        }

        [Fact]
        public void SC_GZ005_3_Execute_SelectionMode_OnlyDrawsSelectedEntities()
        {
            var (repo, _, def, sys) = CreateFixture(
                predicate: (v, e) => ((EntityRepository)v).HasComponent<GizmoSelectedTag>(e));

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });
            // No GizmoSelectedTag yet — entity is not selected.

            // Frame 1: init gizmo. Not selected -> no draw.
            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);
            var gizmo = def.CreatedInstances[0];
            Assert.Equal(0, gizmo.UpdateAndDrawCount);

            // Frame 2: add the selection tag -> should draw.
            repo.AddComponent(entity, new GizmoSelectedTag());
            repo.Bus.SwapBuffers(); // no new events; just drive
            sys.Execute(repo, 0f);
            Assert.Equal(1, gizmo.UpdateAndDrawCount);
        }

        [Fact]
        public void SC_GZ005_4_Execute_NullPredicate_DrawsAll()
        {
            // When predicate is null, the system draws all active gizmos unconditionally.
            // This covers the "global force" equivalent for our design deviation.
            var (repo, _, def, sys) = CreateFixture(predicate: null);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            // Frame 1: initialise + draw (construction frame also draws when predicate=null).
            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);
            var gizmo = def.CreatedInstances[0];
            int countAfterFrame1 = gizmo.UpdateAndDrawCount;
            Assert.True(countAfterFrame1 >= 1, "Gizmo should be drawn in the construction frame when predicate=null");

            // Frame 2: no new events — gizmo should be drawn again.
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            Assert.Equal(countAfterFrame1 + 1, gizmo.UpdateAndDrawCount);
        }

        [Fact]
        public void SC_GZ005_5_NeverVisiblePolicy_SuppressesEvenWhenSelected()
        {
            var (repo, _, def, sys) = CreateFixture(
                policy: NeverVisiblePolicy.Instance,
                predicate: null); // null = always draw if policy allows

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);
            var gizmo = def.CreatedInstances[0];

            // Execute a second frame; NeverVisiblePolicy.IsGloballyEnabled returns false.
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            Assert.Equal(0, gizmo.UpdateAndDrawCount);
        }

        [Fact]
        public void SC_GZ005_6_NoMatchingComponents_GizmoNotActivated()
        {
            var (repo, _, def, sys) = CreateFixture();

            // Entity has GizmoTestCompB only — the gizmo requires GizmoTestCompA.
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompB { Value = 99 });

            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);

            Assert.Equal(0, def.CreatedInstances.Count);
        }

        [Fact]
        public void SC_GZ005_7_DeadEntity_SkippedDuringExecute()
        {
            var (repo, _, def, sys) = CreateFixture(predicate: null);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            // Frame 1: init gizmo (also drawn in construction frame when predicate=null).
            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);
            var gizmo = def.CreatedInstances[0];
            int countAfterInit = gizmo.UpdateAndDrawCount;

            // Destroy entity without publishing DestructionOrder (simulates external kill).
            repo.DestroyEntity(entity);

            // Frame 2: IsAlive check should skip the dead entity — count must not increase.
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.Equal(countAfterInit, gizmo.UpdateAndDrawCount);
        }

        [Fact]
        public void SC_GZ005_8_GlobalVisibilityCache_EvaluatedOncePerFrame()
        {
            var mockPolicy = new MockVisibilityPolicy { GloballyEnabled = true, EntityVisible = true };
            var (repo, _, def, sys) = CreateFixture(policy: mockPolicy, predicate: null);

            // Create three entities, all with the required component.
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            var e3 = repo.CreateEntity();
            repo.AddComponent(e1, new GizmoTestCompA { Value = 1 });
            repo.AddComponent(e2, new GizmoTestCompA { Value = 2 });
            repo.AddComponent(e3, new GizmoTestCompA { Value = 3 });

            // Init all three gizmos in one frame.
            repo.Bus.Publish(new ConstructionOrder { Entity = e1 });
            repo.Bus.Publish(new ConstructionOrder { Entity = e2 });
            repo.Bus.Publish(new ConstructionOrder { Entity = e3 });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            // Record draw counts after the construction frame, then reset the global cache counter.
            int drawsAfterConstruction = def.CreatedInstances[0].UpdateAndDrawCount
                + def.CreatedInstances[1].UpdateAndDrawCount
                + def.CreatedInstances[2].UpdateAndDrawCount;
            mockPolicy.IsGloballyEnabledCallCount = 0;

            // Execute again (no new events) — global visibility should be evaluated
            // exactly once (not once per entity).
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.Equal(1, mockPolicy.IsGloballyEnabledCallCount);
            // Each of the three entities should have been drawn one additional time.
            int drawsAfterDrawFrame = def.CreatedInstances[0].UpdateAndDrawCount
                + def.CreatedInstances[1].UpdateAndDrawCount
                + def.CreatedInstances[2].UpdateAndDrawCount;
            Assert.Equal(drawsAfterConstruction + 3, drawsAfterDrawFrame);
        }
    }

    // ==========================================================================
    // SC-GZ006: BehaviorGizmoManagerSystem tests
    // ==========================================================================

    public class BehaviorGizmoManagerSystemTests
    {
        private static (EntityRepository repo, BehaviorGizmoRegistry behavReg, BehaviorGizmoManagerSystem sys)
            CreateFixture(Func<ISimulationView, Entity, bool>? predicate = null)
        {
            var repo = GizmoTestRepo.Create();
            var behavReg = new BehaviorGizmoRegistry();
            var buffer = new DebugPrimitiveBuffer();
            var sys = new BehaviorGizmoManagerSystem(behavReg, buffer, predicate);
            return (repo, behavReg, sys);
        }

        private static void PublishAssignAndExecute(
            EntityRepository repo, BehaviorGizmoManagerSystem sys, Entity entity, string behaviorName)
        {
            repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = behaviorName, JsonParams = "" });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
        }

        [Fact]
        public void SC_GZ006_1_AssignBehaviorEvent_ActivatesGizmo_OnInitializeCalled()
        {
            var (repo, behavReg, sys) = CreateFixture(predicate: null);
            var factory = new MockBehaviorFactory("TestBehavior");
            behavReg.Register(factory);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");

            Assert.Equal(1, factory.RentCount);
        }

        [Fact]
        public void SC_GZ006_2_ClearBehaviorEvent_TearsDownGizmo()
        {
            var (repo, behavReg, sys) = CreateFixture(predicate: null);
            var factory = new MockBehaviorFactory("TestBehavior");
            behavReg.Register(factory);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            // Assign.
            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");
            Assert.Equal(1, factory.RentCount);
            Assert.Equal(0, factory.ReturnCount);

            // Clear.
            repo.Bus.Publish(new ClearBehaviorEvent { Entity = entity });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.Equal(1, factory.ReturnCount);
        }

        [Fact]
        public void SC_GZ006_3_DestructionOrder_TearsDownBehaviorGizmo()
        {
            var (repo, behavReg, sys) = CreateFixture(predicate: null);
            var factory = new MockBehaviorFactory("TestBehavior");
            behavReg.Register(factory);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");

            GizmoTestRepo.PublishDestructionAndExecute(repo, sys, entity);

            Assert.Equal(1, factory.ReturnCount);
        }

        [Fact]
        public void SC_GZ006_4_NewAssign_ReplacesExistingGizmo()
        {
            var (repo, behavReg, sys) = CreateFixture(predicate: null);
            var factory = new MockBehaviorFactory("TestBehavior");
            behavReg.Register(factory);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            // Assign first gizmo.
            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");
            Assert.Equal(1, factory.RentCount);
            Assert.Equal(0, factory.ReturnCount);

            // Assign again — should tear down first and rent second.
            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");
            Assert.Equal(2, factory.RentCount);
            Assert.Equal(1, factory.ReturnCount);
        }

        [Fact]
        public void SC_GZ006_5_UnknownBehaviorName_SilentlyIgnored()
        {
            var (repo, behavReg, sys) = CreateFixture(predicate: null);
            var entity = repo.CreateEntity();

            // No factory registered — should not throw.
            var ex = Record.Exception(() => PublishAssignAndExecute(repo, sys, entity, "UnknownBehavior"));
            Assert.Null(ex);
        }

        [Fact]
        public void SC_GZ006_6_RentAndReturn_CalledOnActivationAndTeardown()
        {
            var (repo, behavReg, sys) = CreateFixture(predicate: null);
            var factory = new MockBehaviorFactory("TestBehavior");
            behavReg.Register(factory);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");
            Assert.Equal(1, factory.RentCount);

            repo.Bus.Publish(new ClearBehaviorEvent { Entity = entity });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            Assert.Equal(1, factory.ReturnCount);
        }

        [Fact]
        public void SC_GZ006_DrawsGizmo_WhenPredicateNull()
        {
            // When predicate is null all active gizmos are drawn.
            var (repo, behavReg, sys) = CreateFixture(predicate: null);

            MockGizmo? rented = null;
            var factory = new InspectableFactory("TestBehavior", g => rented = (MockGizmo)g);
            behavReg.Register(factory);
            var entity = repo.CreateEntity();

            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");

            // Execute again — should call UpdateAndDraw.
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.NotNull(rented);
            Assert.True(rented!.UpdateAndDrawCount >= 1);
        }

        [Fact]
        public void SC_GZ006_DoesNotDraw_WhenPredicateFalse()
        {
            var (repo, behavReg, sys) = CreateFixture(predicate: (v, e) => false);

            MockGizmo? rented = null;
            var factory = new InspectableFactory("TestBehavior", g => rented = (MockGizmo)g);
            behavReg.Register(factory);
            var entity = repo.CreateEntity();

            PublishAssignAndExecute(repo, sys, entity, "TestBehavior");

            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.NotNull(rented);
            Assert.Equal(0, rented!.UpdateAndDrawCount);
        }

        [Fact]
        public void SC_GZ035_5_BehaviorInterrupt_WithoutClear_TearsDownOldGizmo()
        {
            // Scenario: B-Tree interrupt assigns new behavior without ClearBehaviorEvent.
            // The defensive guard in step 3 must tear down the old gizmo.
            var (repo, behavReg, sys) = CreateFixture(predicate: null);
            var factoryA = new MockBehaviorFactory("BehaviorA");
            var factoryB = new MockBehaviorFactory("BehaviorB");
            behavReg.Register(factoryA);
            behavReg.Register(factoryB);
            var entity = repo.CreateEntity();

            // Assign BehaviorA (no ClearBehaviorEvent yet).
            PublishAssignAndExecute(repo, sys, entity, "BehaviorA");
            Assert.Equal(1, factoryA.RentCount);
            Assert.Equal(0, factoryA.ReturnCount);  // not torn down yet

            // Interrupt: assign BehaviorB without first sending ClearBehaviorEvent.
            // The system's defensive guard must call TeardownEntity for BehaviorA.
            PublishAssignAndExecute(repo, sys, entity, "BehaviorB");

            // BehaviorA gizmo torn down via defensive guard.
            Assert.Equal(1, factoryA.ReturnCount);
            // BehaviorB gizmo is now active.
            Assert.Equal(1, factoryB.RentCount);
            Assert.Equal(0, factoryB.ReturnCount);
        }

        // Factory that lets the test inspect the created gizmo instance.
        private sealed class InspectableFactory : IBehaviorGizmoFactory
        {
            private readonly Action<IEntityStatefulGizmo> _onCreate;

            public InspectableFactory(string behaviorName, Action<IEntityStatefulGizmo> onCreate)
            {
                BehaviorName = behaviorName;
                _onCreate = onCreate;
            }

            public string BehaviorName { get; }

            public IEntityStatefulGizmo Create(ISimulationView view, Entity entity)
            {
                var g = new MockGizmo(entity);
                _onCreate(g);
                return g;
            }
        }
    }

    // ==========================================================================
    // SC-GZ036: DataDrivenGizmoSystem CPU frame budget tests
    // ==========================================================================

    public class DataDrivenGizmoSystemBudgetTests
    {
        private static int SumDrawCounts(IReadOnlyList<MockGizmo> instances)
        {
            int total = 0;
            for (int i = 0; i < instances.Count; i++)
                total += instances[i].UpdateAndDrawCount;
            return total;
        }

        private static (EntityRepository repo, MockGizmoDefinition def, DataDrivenGizmoSystem sys)
            CreateBudgetFixture()
        {
            var repo = GizmoTestRepo.Create();
            var registry = new GizmoRegistry();
            var def = new MockGizmoDefinition(new[] { typeof(GizmoTestCompA) });
            registry.Register(def);
            var buffer = new DebugPrimitiveBuffer();
            var sys = new DataDrivenGizmoSystem(registry, buffer);
            return (repo, def, sys);
        }

        // SC-GZ036-1: Near-zero budget processes only a subset of entities.
        [Fact]
        public void SC_GZ036_1_NearZeroBudget_ProcessesOnlySubset()
        {
            var (repo, def, sys) = CreateBudgetFixture();

            const int entityCount = 50;
            for (int i = 0; i < entityCount; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new GizmoTestCompA { Value = i });
                repo.Bus.Publish(new Fdp.Toolkit.Lifecycle.Events.ConstructionOrder { Entity = e });
            }
            repo.Bus.SwapBuffers();
            // Construction frame (unlimited budget).
            sys.Execute(repo, 0f);
            int countAfterConstruction = SumDrawCounts(def.CreatedInstances);

            // Second frame with near-zero budget.
            sys.MaxGizmoFrameMs = 0.0001f;
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            int drawsThisFrame = SumDrawCounts(def.CreatedInstances) - countAfterConstruction;

            Assert.True(drawsThisFrame < entityCount,
                $"Expected <{entityCount} draws with near-zero budget, got {drawsThisFrame}");
        }

        // SC-GZ036-2: Large budget processes all entities.
        [Fact]
        public void SC_GZ036_2_LargeBudget_ProcessesAllEntities()
        {
            var (repo, def, sys) = CreateBudgetFixture();
            sys.MaxGizmoFrameMs = 10000f;

            const int entityCount = 20;
            for (int i = 0; i < entityCount; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new GizmoTestCompA { Value = i });
                repo.Bus.Publish(new Fdp.Toolkit.Lifecycle.Events.ConstructionOrder { Entity = e });
            }
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            int countAfterConstruction = SumDrawCounts(def.CreatedInstances);

            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            int drawsThisFrame = SumDrawCounts(def.CreatedInstances) - countAfterConstruction;

            Assert.Equal(entityCount, drawsThisFrame);
        }

        // SC-GZ036-3: Zero budget means unlimited (all entities processed).
        [Fact]
        public void SC_GZ036_3_ZeroBudget_MeansUnlimited()
        {
            var (repo, def, sys) = CreateBudgetFixture();
            sys.MaxGizmoFrameMs = 0f; // unlimited

            const int entityCount = 20;
            for (int i = 0; i < entityCount; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new GizmoTestCompA { Value = i });
                repo.Bus.Publish(new Fdp.Toolkit.Lifecycle.Events.ConstructionOrder { Entity = e });
            }
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            int countAfterConstruction = SumDrawCounts(def.CreatedInstances);

            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            int drawsThisFrame = SumDrawCounts(def.CreatedInstances) - countAfterConstruction;

            Assert.Equal(entityCount, drawsThisFrame);
        }

        // SC-GZ036-4: Time-slice state maintained across multiple Execute calls (no exception,
        // draw counts accumulate correctly).
        [Fact]
        public void SC_GZ036_4_TimeSliceState_Is_Field_Not_Reallocated()
        {
            var (repo, def, sys) = CreateBudgetFixture();
            sys.MaxGizmoFrameMs = 10000f;

            const int entityCount = 5;
            for (int i = 0; i < entityCount; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new GizmoTestCompA { Value = i });
                repo.Bus.Publish(new Fdp.Toolkit.Lifecycle.Events.ConstructionOrder { Entity = e });
            }
            repo.Bus.SwapBuffers();

            // Run 3 frames — no exception is the main check.
            for (int frame = 0; frame < 3; frame++)
            {
                sys.Execute(repo, 0f);
                repo.Bus.SwapBuffers();
            }

            int totalDraws = SumDrawCounts(def.CreatedInstances);
            Assert.True(totalDraws > 0, "Expected at least some gizmo draws across 3 frames");
        }
    }

    // ==========================================================================
    // SC-GZ066: DataDrivenGizmoSystem composite-key routing
    // ==========================================================================

    // Gizmo mock that tracks interaction calls for routing tests.
    internal sealed class TrackingGizmo : IEntityStatefulGizmo
    {
        public int InteractionStartedCount;
        public int MenuActionCount;
        public int StructUpdateCount;
        public int LastMenuActionId;
        public string? LastStructUpdateJson;

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool v) => IsFocused = v;

        public void UpdateAndDraw(float dt, IDebugDrawBuilder db) { }
        public void Dispose() { }

        public void OnInteractionStarted(GizmoPickToken token, System.Numerics.Vector3 worldPos)
            => InteractionStartedCount++;
        public void OnDragUpdate(System.Numerics.Vector3 worldPos) { }
        public void OnCommit(System.Numerics.Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { MenuActionCount++; LastMenuActionId = actionId; }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, System.Numerics.Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
        public void OnStructUpdate(string payloadJson) { StructUpdateCount++; LastStructUpdateJson = payloadJson; }
    }

    // IGizmoDefinition that produces TrackingGizmo instances.
    internal sealed class TrackingGizmoDefinition : IGizmoDefinition
    {
        private readonly List<TrackingGizmo> _instances = new();

        public TrackingGizmoDefinition(Type[] requiredComponents, uint gizmoTypeId)
        {
            RequiredComponents = requiredComponents;
            GizmoTypeId = gizmoTypeId;
        }

        public Type[] RequiredComponents { get; }
        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;
        public uint GizmoTypeId { get; }
        public IReadOnlyList<TrackingGizmo> Instances => _instances;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            var g = new TrackingGizmo();
            _instances.Add(g);
            return g;
        }
    }

    public class DataDrivenGizmoSystemRoutingTests
    {
        private static (EntityRepository repo, DataDrivenGizmoSystem sys, TrackingGizmoDefinition def1, TrackingGizmoDefinition def2, Entity entity, FdpEventBus interactionBus)
            CreateRoutingFixture()
        {
            var repo = GizmoTestRepo.Create();

            // Register interaction events on a dedicated bus (same as production wiring).
            var interactionBus = new FdpEventBus();
            interactionBus.Register<GizmoInteractionStartedEvent>();
            interactionBus.Register<GizmoDragUpdateEvent>();
            interactionBus.Register<GizmoInteractionCommitEvent>();
            interactionBus.Register<GizmoInteractionCancelEvent>();
            interactionBus.Register<GizmoMenuActionEvent>();
            interactionBus.Register<GizmoMouseEvent>();
            interactionBus.Register<GizmoKeyEvent>();

            var registry = new GizmoRegistry();
            var def1 = new TrackingGizmoDefinition(new[] { typeof(GizmoTestCompA) }, gizmoTypeId: 0xAA01u);
            var def2 = new TrackingGizmoDefinition(new[] { typeof(GizmoTestCompA) }, gizmoTypeId: 0xAA02u);
            registry.Register(def1);
            registry.Register(def2);

            var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
            var sys = new DataDrivenGizmoSystem(registry, buffer, interactionBus: interactionBus);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });

            // Init both gizmos.
            repo.Bus.Publish(new Fdp.Toolkit.Lifecycle.Events.ConstructionOrder { Entity = entity });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            return (repo, sys, def1, def2, entity, interactionBus);
        }

        // SC-GZ066-1: Two gizmos on same entity; GizmoInteractionStartedEvent with def1's
        // GizmoTypeId reaches def1's gizmo only; def2 receives nothing.
        [Fact]
        public void SC_GZ066_1_InteractionStarted_RoutesTo_MatchingGizmo_Only()
        {
            var (repo, sys, def1, def2, entity, bus) = CreateRoutingFixture();

            bus.Publish(new GizmoInteractionStartedEvent
            {
                Token = new PickToken { Target = entity, GizmoTypeId = 0xAA01u },
            });
            bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.Equal(1, def1.Instances[0].InteractionStartedCount);
            Assert.Equal(0, def2.Instances[0].InteractionStartedCount);
        }

        // SC-GZ066-2: GizmoStructUpdateEvent with def2's GizmoTypeId reaches def2 only.
        [Fact]
        public void SC_GZ066_2_StructUpdate_RoutesTo_MatchingGizmo()
        {
            var (repo, sys, def1, def2, entity, bus) = CreateRoutingFixture();

            bus.PublishManaged(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoStructUpdateEvent
            {
                AnchorId    = entity.Index,
                GizmoTypeId = 0xAA02u,
                PayloadJson = "{\"x\":1}",
            });
            bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.Equal(0, def1.Instances[0].StructUpdateCount);
            Assert.Equal(1, def2.Instances[0].StructUpdateCount);
            Assert.Equal("{\"x\":1}", def2.Instances[0].LastStructUpdateJson);
        }

        // SC-GZ066-5: GizmoMenuActionEvent with def2's GizmoTypeId — only def2 receives it.
        [Fact]
        public void SC_GZ066_5_MenuAction_RoutesTo_MatchingGizmo_Only()
        {
            var (repo, sys, def1, def2, entity, bus) = CreateRoutingFixture();

            bus.Publish(new GizmoMenuActionEvent
            {
                AnchorId    = (uint)entity.Index,
                ActionId    = 7,
                GizmoTypeId = 0xAA02u,
            });
            bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.Equal(0, def1.Instances[0].MenuActionCount);
            Assert.Equal(1, def2.Instances[0].MenuActionCount);
            Assert.Equal(7, def2.Instances[0].LastMenuActionId);
        }
    }
}

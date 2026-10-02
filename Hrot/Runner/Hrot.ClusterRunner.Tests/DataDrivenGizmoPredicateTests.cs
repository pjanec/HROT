using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Xunit;

namespace Hrot.ClusterRunner.Tests
{
    // ========================================================================
    // D-003: Verify DataDrivenGizmoSystem predicate filtering
    //
    // The predicate is wired at the registration site in Hrot.ClusterRunner.
    // DataDrivenGizmoSystem and BehaviorGizmoManagerSystem are NOT yet
    // registered in Hrot.ClusterRunner (no registration site exists as of
    // BATCH-06). The test below verifies the predicate contract at the
    // system level, ready for future wiring.
    // ========================================================================

    // ---- Test-only component (ID 248 - free range) -------------------------

    [ComponentId(248)]
    public struct D003FilterTestComp { public int Value; }

    // ---- Minimal mocks -----------------------------------------------------

    internal sealed class D003MockGizmo : IEntityStatefulGizmo
    {
        public int UpdateAndDrawCount;

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder drawBuilder)
        {
            UpdateAndDrawCount++;
        }

        public void Dispose() { }

        // IGizmoInteractionHandler stubs
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
    }

    internal sealed class D003GizmoDef : IGizmoDefinition
    {
        private D003MockGizmo? _instance;

        public D003MockGizmo? LastInstance => _instance;

        public Type[] RequiredComponents => new[] { typeof(D003FilterTestComp) };
        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;
        public uint GizmoTypeId => 0xD003u;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            _instance = new D003MockGizmo();
            return _instance;
        }
    }

    // 🔴🔴 D003NoOpDrawBuilder RETIRED 2026-09-10 (CE-259ab). It hand-rolled IDebugDrawBuilder, and
    //   DataDrivenGizmoSystem stamps GizmoTypeId through a DebugPrimitiveBuffer -- so with this stub the
    //   whole Execute() call threw InvalidCastException before either assertion below could run, and both
    //   D003 tests had been RED for as long as the cast existed.
    //   ⛔ Fixing the SYSTEM to degrade instead of throw is only half the answer (R-142 ③): a stub that
    //     cannot stamp makes this rail BLIND to composite-key routing rather than red. ⇒ use the real
    //     builder -- there is exactly ONE production IDebugDrawBuilder and it is DebugPrimitiveBuffer,
    //     so the stub was measuring a configuration production never has.
    //   ⭐ It is not a heavier fixture: a capacity-64 buffer is a plain array, and it lets a future rail
    //     here assert what was actually EMITTED instead of only counting calls.

    // ---- Test class --------------------------------------------------------

    public class DataDrivenGizmoPredicateTests
    {
        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<D003FilterTestComp>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        [Fact]
        public void D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity()
        {
            // Arrange
            using var repo = CreateRepo();

            var def = new D003GizmoDef();
            var registry = new GizmoRegistry();
            registry.Register(def);

            var draw = new DebugPrimitiveBuffer(capacity: 64);

            // Predicate: always false -> all entities filtered out
            var sys = new DataDrivenGizmoSystem(registry, draw,
                isSelectedPredicate: (_, _) => false);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new D003FilterTestComp { Value = 1 });

            // Publish construction order and advance bus
            repo.Bus.Publish(new ConstructionOrder { Entity = entity });
            repo.Bus.SwapBuffers();

            // Act: execute (gizmo initialised but predicate blocks UpdateAndDraw)
            sys.Execute(repo, 0f);

            // Assert: gizmo was created (init happened) but UpdateAndDraw was never called
            Assert.NotNull(def.LastInstance);
            Assert.Equal(0, def.LastInstance!.UpdateAndDrawCount);
        }

        [Fact]
        public void D003_Predicate_True_AllowsUpdateAndDraw()
        {
            // Arrange
            using var repo = CreateRepo();

            var def = new D003GizmoDef();
            var registry = new GizmoRegistry();
            registry.Register(def);

            var draw = new DebugPrimitiveBuffer(capacity: 64);

            // Predicate: always true -> all entities pass
            var sys = new DataDrivenGizmoSystem(registry, draw,
                isSelectedPredicate: (_, _) => true);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new D003FilterTestComp { Value = 1 });

            repo.Bus.Publish(new ConstructionOrder { Entity = entity });
            repo.Bus.SwapBuffers();

            sys.Execute(repo, 0f);

            Assert.NotNull(def.LastInstance);
            Assert.Equal(1, def.LastInstance!.UpdateAndDrawCount);
        }
    }
}

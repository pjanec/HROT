using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;
using Fdp.Toolkit.Lifecycle.Events;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ==========================================================================
    // Test helpers for GZ039
    // ==========================================================================

    internal sealed class MockUndoRecord : IGizmoUndoRecord
    {
        public string Description => "Mock";
        public int UndoCallCount { get; private set; }
        public int RedoCallCount { get; private set; }
        public void Undo(IEntityCommandBuffer cmd) => UndoCallCount++;
        public void Redo(IEntityCommandBuffer cmd) => RedoCallCount++;
    }

    /// <summary>
    /// A stateful gizmo that returns a configurable undo record from CreateUndoRecord.
    /// </summary>
    internal sealed class MockUndoGizmo : IEntityStatefulGizmo
    {
        private readonly IGizmoUndoRecord? _record;

        public MockUndoGizmo(IGizmoUndoRecord? record) => _record = record;

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder drawBuilder) { }
        public void Dispose() { }

        // IGizmoInteractionHandler stubs
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }

        public IGizmoUndoRecord? CreateUndoRecord(GizmoInteractionCommitEvent commit) => _record;
    }

    internal sealed class MockUndoGizmoDefinition : IGizmoDefinition
    {
        private readonly IGizmoUndoRecord? _record;

        public MockUndoGizmoDefinition(IGizmoUndoRecord? record)
        {
            _record = record;
        }

        public System.Type[] RequiredComponents => new[] { typeof(GizmoTestCompA) };
        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;
        public uint GizmoTypeId => 1u;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity) => new MockUndoGizmo(_record);
    }

    // ==========================================================================
    // SC-GZ039: GizmoUndoStack tests
    // ==========================================================================

    public class GizmoUndoStackTests
    {
        [Fact]
        public void SC_GZ039_1_Push_Then_Undo_CallsUndoAndMovesRecord()
        {
            var stack = new GizmoUndoStack();
            var record = new MockUndoRecord();
            stack.Push(record);

            stack.Undo(null!); // cmd is null — MockUndoRecord ignores it

            Assert.Equal(1, record.UndoCallCount);
            Assert.False(stack.CanUndo);
            Assert.True(stack.CanRedo);
        }

        [Fact]
        public void SC_GZ039_2_Undo_Then_Redo_CallsRedoAndMovesBack()
        {
            var stack = new GizmoUndoStack();
            var record = new MockUndoRecord();
            stack.Push(record);
            stack.Undo(null!);

            stack.Redo(null!);

            Assert.Equal(1, record.RedoCallCount);
            Assert.True(stack.CanUndo);
            Assert.False(stack.CanRedo);
        }

        [Fact]
        public void SC_GZ039_3_Push_BeyondMaxDepth_DropsOldest()
        {
            var stack = new GizmoUndoStack { MaxDepth = 3 };
            var r1 = new MockUndoRecord();
            var r2 = new MockUndoRecord();
            var r3 = new MockUndoRecord();
            var r4 = new MockUndoRecord();

            stack.Push(r1); stack.Push(r2); stack.Push(r3);
            stack.Push(r4); // this should drop r1

            // Undo 3 times: expect r4, r3, r2 — NOT r1.
            stack.Undo(null!); // r4
            stack.Undo(null!); // r3
            stack.Undo(null!); // r2
            Assert.False(stack.CanUndo); // r1 was evicted
            Assert.Equal(1, r2.UndoCallCount);
            Assert.Equal(0, r1.UndoCallCount); // r1 was dropped
        }

        [Fact]
        public void SC_GZ039_4_Push_ClearsRedoStack()
        {
            var stack = new GizmoUndoStack();
            var r1 = new MockUndoRecord();
            var r2 = new MockUndoRecord();
            stack.Push(r1);
            stack.Undo(null!); // r1 moves to redo
            Assert.True(stack.CanRedo);

            stack.Push(r2); // new action invalidates redo

            Assert.False(stack.CanRedo);
            Assert.True(stack.CanUndo);
        }

        [Fact]
        public void SC_GZ039_5_Undo_WhenEmpty_NoOp()
        {
            var stack = new GizmoUndoStack();
            stack.Undo(null!); // must not throw
            Assert.False(stack.CanUndo);
        }

        [Fact]
        public void SC_GZ039_6_Redo_WhenEmpty_NoOp()
        {
            var stack = new GizmoUndoStack();
            stack.Redo(null!); // must not throw
            Assert.False(stack.CanRedo);
        }

        [Fact]
        public void SC_GZ039_7_DataDrivenGizmoSystem_PushesRecord_AfterCommit()
        {
            using var repo = GizmoTestRepo.Create();
            repo.RegisterEvent<GizmoInteractionCommitEvent>();

            var undoStack = new GizmoUndoStack();
            var record    = new MockUndoRecord();
            var def       = new MockUndoGizmoDefinition(record);
            var registry  = new GizmoRegistry();
            registry.Register(def);
            var buffer = new DebugPrimitiveBuffer();
            var sys = new DataDrivenGizmoSystem(registry, buffer, null, undoStack);

            // Create entity and publish ConstructionOrder to initialize the gizmo.
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });
            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);

            // Publish a commit event for that entity.
            var token = new PickToken { Target = entity, SubElementId = 0 };
            repo.Bus.Publish(new GizmoInteractionCommitEvent { Token = token, WorldPos = default });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.True(undoStack.CanUndo);
        }

        [Fact]
        public void SC_GZ039_8_Null_CreateUndoRecord_DoesNotPush()
        {
            using var repo = GizmoTestRepo.Create();
            repo.RegisterEvent<GizmoInteractionCommitEvent>();

            var undoStack = new GizmoUndoStack();
            var def       = new MockUndoGizmoDefinition(null); // returns null record
            var registry  = new GizmoRegistry();
            registry.Register(def);
            var buffer = new DebugPrimitiveBuffer();
            var sys = new DataDrivenGizmoSystem(registry, buffer, null, undoStack);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new GizmoTestCompA { Value = 1 });
            GizmoTestRepo.PublishConstructionAndExecute(repo, sys, entity);

            var token = new PickToken { Target = entity, SubElementId = 0 };
            repo.Bus.Publish(new GizmoInteractionCommitEvent { Token = token, WorldPos = default });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.False(undoStack.CanUndo);
        }
    }
}

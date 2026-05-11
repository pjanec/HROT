// SC-B28-1 through SC-B28-3: DataDrivenGizmoSystem emits InputCaptureBinding
// for the exclusive-focus gizmo each frame.
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // Mock gizmo whose RequiresExclusiveFocus is configurable at construction.
    internal sealed class ExclusiveMockGizmo : IEntityStatefulGizmo
    {
        public bool RequiresExclusiveFocus { get; }
        public bool IsFocused { get; private set; }

        public ExclusiveMockGizmo(bool exclusive = true)
        {
            RequiresExclusiveFocus = exclusive;
        }

        public void SetFocus(bool isFocused) => IsFocused = isFocused;
        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder drawBuilder) { }
        public void Dispose() { }
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
    }

    public sealed class DataDrivenGizmoSystemBindingTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly DataDrivenGizmoSystem _sys;
        private readonly Entity _entity;

        public DataDrivenGizmoSystemBindingTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterEvent<ConstructionOrder>();
            _repo.RegisterEvent<DestructionOrder>();
            _repo.RegisterEvent<GizmoComponentActivatedEvent>();

            var registry = new GizmoRegistry();
            _buffer = new DebugPrimitiveBuffer(64);
            _sys = new DataDrivenGizmoSystem(registry, _buffer, isSelectedPredicate: null);

            _entity = _repo.CreateEntity();
        }

        public void Dispose() => _repo.Dispose();

        // SC-B28-1: Executing with an exclusive-focus injected gizmo emits one
        // InputCaptureBinding primitive (ConditionMask == 1).
        [Fact]
        public void SC_B28_1_EmitsInputCaptureBinding_ForExclusiveFocusGizmo()
        {
            var gizmo = new ExclusiveMockGizmo(exclusive: true);
            _sys.ActivateGizmo(_entity, gizmo);

            _repo.Bus.SwapBuffers();
            _sys.Execute(_repo, 0f);

            bool found = false;
            foreach (ref readonly var prim in _buffer.GetFrame())
            {
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding && prim.ConditionMask == 1u)
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Expected an InputCaptureBinding with ConditionMask==1 in the buffer.");
        }

        // SC-B28-2: No InputCaptureBinding is emitted when the gizmo does not require
        // exclusive focus.
        [Fact]
        public void SC_B28_2_DoesNotEmitInputCaptureBinding_WhenNoExclusiveFocus()
        {
            var gizmo = new ExclusiveMockGizmo(exclusive: false);
            _sys.ActivateGizmo(_entity, gizmo);

            _repo.Bus.SwapBuffers();
            _sys.Execute(_repo, 0f);

            foreach (ref readonly var prim in _buffer.GetFrame())
            {
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding)
                    Assert.Fail("Unexpected InputCaptureBinding in buffer for non-exclusive gizmo.");
            }
        }

        // SC-B28-3: The InputCaptureBinding primitive carries the entity Index as NetworkId.
        [Fact]
        public void SC_B28_3_InputCaptureBinding_HasEntityIndexAsNetworkId()
        {
            var gizmo = new ExclusiveMockGizmo(exclusive: true);
            _sys.ActivateGizmo(_entity, gizmo);

            _repo.Bus.SwapBuffers();
            _sys.Execute(_repo, 0f);

            foreach (ref readonly var prim in _buffer.GetFrame())
            {
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding && prim.ConditionMask == 1u)
                {
                    Assert.Equal((long)_entity.Index, prim.StructNetworkId);
                    return;
                }
            }
            Assert.Fail("No InputCaptureBinding primitive found in buffer.");
        }
    }
}

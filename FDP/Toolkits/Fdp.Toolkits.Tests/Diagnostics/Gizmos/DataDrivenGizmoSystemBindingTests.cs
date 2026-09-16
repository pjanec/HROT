// SC-B28-1 through SC-B28-3: DataDrivenGizmoSystem emits InputCaptureBinding
// for the exclusive-focus gizmo each frame.
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
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
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder drawBuilder) { }
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

            // ⭐⭐ §6.8 — an exclusive-focus gizmo's InputCaptureBinding is KEYED BY the anchor's network
            //   id, so the fixture entity must have one. ⛔ It used to be a bare CreateEntity(), which is
            //   why SC-B28-3 below could ever have been named "HasEntityIndexAsNetworkId".
            _repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            _entity = _repo.CreateEntity();
            _repo.AddComponent(_entity, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = NetId });
        }

        /// <summary>⭐ Deliberately far from any ECS index this fixture allocates, so a rail cannot pass
        /// by coincidence if the two id domains are ever confused again.</summary>
        private const long NetId = 7041L;

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

        // SC-B28-3: ⭐⭐⭐ The InputCaptureBinding carries the anchor's NETWORK ID.
        //   ⛔⛔ RENAMED 2026-09-11 (§6.8). It was `SC_B28_3_InputCaptureBinding_HasEntityIndexAsNetworkId`
        //     and it asserted `Assert.Equal((long)_entity.Index, prim.StructNetworkId)` — i.e. THE RAIL'S
        //     OWN NAME AND BODY PINNED DEFECT D1 IN PLACE: an ECS index used as a network id. S5 fixed the
        //     production side and left this rail asserting a coincidence (on a bare entity both were 0).
        //   ⭐ NetId is 7041, nothing like an index, so the two domains can no longer be confused silently.
        //   ⛔ RED-PROOF SHAPE: key the binding by `entity.Index` again and this reddens.
        [Fact]
        public void SC_B28_3_InputCaptureBinding_CarriesTheAnchorNetworkId()
        {
            var gizmo = new ExclusiveMockGizmo(exclusive: true);
            _sys.ActivateGizmo(_entity, gizmo);

            _repo.Bus.SwapBuffers();
            _sys.Execute(_repo, 0f);

            foreach (ref readonly var prim in _buffer.GetFrame())
            {
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding && prim.ConditionMask == 1u)
                {
                    Assert.Equal(NetId, prim.StructNetworkId);
                    return;
                }
            }
            Assert.Fail("No InputCaptureBinding primitive found in buffer.");
        }
    }
}

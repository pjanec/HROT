// SC-B29-1 through SC-B29-5: GlobalGizmoManager lifecycle and event routing.
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ---- Minimal mock gizmo ------------------------------------------------

    internal sealed class MockGlobalGizmo : IEntityStatefulGizmo
    {
        public bool RequiresExclusiveFocus { get; set; } = true;
        public bool IsFocused { get; private set; }
        public bool FocusGranted   { get; private set; }
        public bool FocusRevoked   { get; private set; }
        public bool Disposed       { get; private set; }
        public int  DragCount      { get; private set; }
        public int  MouseCount     { get; private set; }
        public MapMouseButton LastMouseButton { get; private set; }
        public bool LastMousePressed         { get; private set; }

        public void SetFocus(bool f)  { IsFocused = f; if (f) FocusGranted = true; else FocusRevoked = true; }
        public void UpdateAndDraw(float dt, IDebugDrawBuilder b) { }
        public void OnDragUpdate(Vector3 pos)                              { DragCount++; }
        public void OnMouseEvent(MapMouseButton btn, bool p, Vector3 w)   { MouseCount++; LastMouseButton = btn; LastMousePressed = p; }
        public void OnKeyEvent(MapKeyboardKey k, bool p)                  { }
        public void OnInteractionStarted(GizmoPickToken t, Vector3 w)     { }
        public void OnCommit(Vector3 w)                                    { }
        public void OnCancel()                                             { }
        public void OnMenuAction(int id)                                   { }
        public void Dispose() { Disposed = true; }
    }

    // ---- Tests -----------------------------------------------------------

    public class GlobalGizmoManagerTests
    {
        private static DebugPrimitiveBuffer MakeBuffer() => new DebugPrimitiveBuffer();

        // SC-B29-1: Register grants exclusive focus.
        [Fact]
        public void Register_GrantsFocusToExclusiveGizmo()
        {
            var manager = new GlobalGizmoManager(MakeBuffer());
            var gizmo   = new MockGlobalGizmo { RequiresExclusiveFocus = true };

            manager.Register(1L, gizmo);

            Assert.True(gizmo.FocusGranted);
            Assert.True(gizmo.IsFocused);
        }

        // SC-B29-2: Execute emits InputCaptureBinding for focused gizmo.
        [Fact]
        public void Execute_EmitsInputCaptureBinding_ForFocusedGizmo()
        {
            var buffer  = MakeBuffer();
            var manager = new GlobalGizmoManager(buffer);
            var gizmo   = new MockGlobalGizmo { RequiresExclusiveFocus = true };

            manager.Register(42L, gizmo);

            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoDragUpdateEvent>();
            repo.RegisterEvent<GizmoMouseEvent>();
            repo.RegisterEvent<GizmoKeyEvent>();
            repo.Bus.SwapBuffers();

            manager.Execute(repo, 0.016f);

            bool found = false;
            foreach (ref readonly var prim in buffer.GetFrame())
            {
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding && prim.ConditionMask == 1u)
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Expected InputCaptureBinding(exclusive) in buffer.");
        }

        // SC-B29-3: Unregister disposes gizmo and clears focus.
        [Fact]
        public void Unregister_DisposesGizmoAndClearsFocus()
        {
            var manager = new GlobalGizmoManager(MakeBuffer());
            var gizmo   = new MockGlobalGizmo { RequiresExclusiveFocus = true };

            manager.Register(7L, gizmo);
            manager.Unregister(7L);

            Assert.True(gizmo.Disposed);
            Assert.True(gizmo.FocusRevoked);
            Assert.Equal(0, manager.ActiveCount);
        }

        // SC-B29-4: Unregister with unknown id is safe no-op.
        [Fact]
        public void Unregister_UnknownId_IsNoOp()
        {
            var manager = new GlobalGizmoManager(MakeBuffer());

            var ex = Record.Exception(() => manager.Unregister(999L));
            Assert.Null(ex);
        }

        // SC-B29-5: Execute routes GizmoMouseEvent to focused gizmo.
        [Fact]
        public void Execute_RoutesMouseEventToFocusedGizmo()
        {
            var buffer  = MakeBuffer();
            var manager = new GlobalGizmoManager(buffer);
            var gizmo   = new MockGlobalGizmo { RequiresExclusiveFocus = true };
            manager.Register(1L, gizmo);

            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoDragUpdateEvent>();
            repo.RegisterEvent<GizmoMouseEvent>();
            repo.RegisterEvent<GizmoKeyEvent>();

            // Publish a mouse event and swap so it is visible in the read buffer.
            repo.Bus.Publish(new GizmoMouseEvent
            {
                Button    = MapMouseButton.Left,
                IsPressed = false,
                WorldPos  = new Vector3(1f, 2f, 0f),
            });
            repo.Bus.SwapBuffers();

            manager.Execute(repo, 0.016f);

            Assert.Equal(1, gizmo.MouseCount);
            Assert.Equal(MapMouseButton.Left, gizmo.LastMouseButton);
        }
    }
}

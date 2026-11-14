// SC-B29-1 through SC-B29-5: GlobalGizmoManager lifecycle and event routing.
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
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
        public void UpdateAndDraw(ISimulationView view, float dt, IDebugDrawBuilder b) { }
        public void OnDragUpdate(Vector3 pos)                              { DragCount++; }
        public void OnMouseEvent(MapMouseButton btn, bool p, Vector3 w)   { MouseCount++; LastMouseButton = btn; LastMousePressed = p; }
        public void OnKeyEvent(MapKeyboardKey k, bool p)                  { }
        public void OnInteractionStarted(GizmoPickToken t, Vector3 w)     { }
        public void OnCommit(Vector3 w)                                    { }
        public void OnCancel()                                             { }
        public void OnMenuAction(int id)                                   { MenuActions.Add(id); }
        public string? ToolMenuJson { get; set; }                          // ⭐ B-17 P2 — the tool's own right-click menu
        public System.Collections.Generic.List<int> MenuActions { get; } = new();
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

        /// <summary>
        /// ⭐ B-17 P2 (📄 docs/UX/DESIGN_Map_Interaction.md §2.5, D11) — a tool that holds the input and has a menu of its own (the drawing menu) gets it BOUND to its
        /// tool id (the capture anchor), so a right-click on the map opens it; and a click on a row, anchored at that id, reaches THAT gizmo's OnMenuAction.
        /// A gizmo with no menu binds nothing, and a menu click anchored elsewhere reaches nobody.
        /// </summary>
        [Fact]
        public void B17P2_AToolsOwnMenu_IsBoundToItsToolId_AndItsClicksReachIt()
        {
            var buffer  = MakeBuffer();
            var manager = new GlobalGizmoManager(buffer);
            var drawing = new MockGlobalGizmo { RequiresExclusiveFocus = true, ToolMenuJson = "[{\"id\":2410,\"label\":\"Finish\"}]" };
            var other   = new MockGlobalGizmo { RequiresExclusiveFocus = false };   // no menu of its own
            long id = GlobalGizmoManager.NewId();
            manager.Register(id, drawing);
            manager.Register(GlobalGizmoManager.NewId(), other);

            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoDragUpdateEvent>();
            repo.RegisterEvent<GizmoMouseEvent>();
            repo.RegisterEvent<GizmoKeyEvent>();
            repo.RegisterEvent<GizmoMenuActionEvent>();
            repo.Bus.Publish(new GizmoMenuActionEvent { AnchorId = id, ActionId = 2410 });
            repo.Bus.Publish(new GizmoMenuActionEvent { AnchorId = id + 12345, ActionId = 2412 });   // somebody else's menu
            repo.Bus.SwapBuffers();

            manager.Execute(repo, 0.016f);

            int bindings = 0; long boundTo = 0;
            foreach (ref readonly var prim in buffer.GetFrame())
                if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding) { bindings++; boundTo = prim.StructNetworkId; }
            Assert.Equal(1, bindings);                       // only the gizmo that has a menu
            Assert.Equal(id, boundTo);                       // keyed by its tool id — the capture anchor
            Assert.Equal(new[] { 2410 }, drawing.MenuActions.ToArray());   // the click anchored at its id arrived; the other did not
            Assert.Empty(other.MenuActions);
        }
    }
}

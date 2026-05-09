using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // IGizmoDefinition for interactive EditablePolyline vertex editing.
    // Activated by DataDrivenGizmoSystem when SimTransform + ActiveVertexEditRequest
    // are both present on an entity.
    // Only instantiates a gizmo if the entity also has an EditablePolyline managed component.
    public sealed class VertexEditGizmoDefinition : IGizmoDefinition
    {
        public Type[] RequiredComponents { get; } =
        {
            typeof(SimTransform),
            typeof(ActiveVertexEditRequest),
        };

        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            var repo = view as EntityRepository
                ?? throw new ArgumentException(
                    $"{nameof(VertexEditGizmoDefinition)}.CreateInstance requires " +
                    $"direct EntityRepository access, not {view.GetType().Name}.");

            // Only create a gizmo for entities that actually have an EditablePolyline.
            if (!repo.HasManagedComponent<EditablePolyline>(entity))
                return new NullGizmo();

            long networkId = 0;
            if (repo.HasComponent<NetworkIdentity>(entity))
                networkId = repo.GetComponentRO<NetworkIdentity>(entity).Value;

            return new VertexEditGizmo(
                view,
                entity,
                networkId,
                onRemove: () =>
                {
                    if (repo.IsAlive(entity) && repo.HasComponent<ActiveVertexEditRequest>(entity))
                        repo.RemoveComponent<ActiveVertexEditRequest>(entity);
                });
        }

        // No-op gizmo returned when the entity lacks EditablePolyline (safety guard).
        private sealed class NullGizmo : IEntityStatefulGizmo
        {
            public bool RequiresExclusiveFocus => false;
            public bool IsFocused { get; private set; }
            public void SetFocus(bool f) => IsFocused = f;
            public void UpdateAndDraw(float dt, IDebugDrawBuilder draw) { }
            public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
            public void OnDragUpdate(Vector3 worldPos) { }
            public void OnCommit(Vector3 worldPos) { }
            public void OnCancel() { }
            public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
            public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
            public void OnMenuAction(int actionId) { }
            public void Dispose() { }
        }
    }
}

using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.SimHost.Gizmos
{
    // IGizmoDefinition for interactive entity rotation.
    // Activated by DataDrivenGizmoSystem when both SimTransform and
    // ActiveRotationToolRequest are present on the same entity.
    // The gizmo removes ActiveRotationToolRequest via its onRemove callback
    // to signal that the interaction is complete, which causes the system
    // to tear it down automatically on the next frame.
    public sealed class EntityRotatorGizmoDefinition : IGizmoDefinition
    {
        public Type[] RequiredComponents { get; } =
        {
            typeof(SimTransform),
            typeof(ActiveRotationToolRequest),
        };

        // Always visible while active (exclusive-focus gizmo; never filtered by policy).
        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
        {
            var repo = view as EntityRepository
                ?? throw new ArgumentException(
                    $"{nameof(EntityRotatorGizmoDefinition)}.CreateInstance requires " +
                    $"direct EntityRepository access, not {view.GetType().Name}.");

            return new EntityRotatorGizmo(
                view,
                entity,
                onRemove: () =>
                {
                    if (repo.IsAlive(entity) && repo.HasComponent<ActiveRotationToolRequest>(entity))
                        repo.RemoveComponent<ActiveRotationToolRequest>(entity);
                });
        }
    }
}

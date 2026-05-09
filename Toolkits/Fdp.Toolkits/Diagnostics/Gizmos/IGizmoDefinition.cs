using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// Describes a type of entity-bound gizmo: which components an entity must carry,
    /// which visibility policy governs it, and how to create a fresh instance.
    /// Registered with <see cref="GizmoRegistry"/> at startup.
    /// </summary>
    public interface IGizmoDefinition
    {
        /// <summary>
        /// Component types the entity must have for this gizmo to activate.
        /// Every type must be registered with <see cref="Fdp.Core.ComponentTypeRegistry"/>
        /// before <see cref="GizmoRegistry.Register"/> is called.
        /// </summary>
        Type[] RequiredComponents { get; }

        /// <summary>Governs when this gizmo is visible.</summary>
        IGizmoVisibilityPolicy VisibilityPolicy { get; }

        /// <summary>
        /// Creates a new gizmo instance bound to the given entity.
        /// The view and entity are stored by the implementation; they do not appear on
        /// <see cref="IEntityStatefulGizmo.UpdateAndDraw"/>.
        /// </summary>
        IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity);
    }
}

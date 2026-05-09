using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// Factory for behavior-bound gizmo instances.
    /// Registered with <see cref="BehaviorGizmoRegistry"/> by behavior name.
    /// Pooling has been dropped per gizmo-input-focus-design.md §12: use plain new + Dispose.
    /// </summary>
    public interface IBehaviorGizmoFactory
    {
        /// <summary>
        /// The behavior name this factory handles. Must match the value in
        /// <see cref="Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent.BehaviorName"/>.
        /// </summary>
        string BehaviorName { get; }

        /// <summary>
        /// Creates a new gizmo instance bound to the given entity.
        /// Called when an <see cref="Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent"/> arrives.
        /// The returned gizmo is owned by the manager; <see cref="IEntityStatefulGizmo.Dispose"/>
        /// is called when the behavior is cleared or the entity is destroyed.
        /// </summary>
        IEntityStatefulGizmo Create(ISimulationView view, Entity entity);
    }
}

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// Pool factory for behavior-bound gizmo instances.
    /// Registered with <see cref="BehaviorGizmoRegistry"/> by behavior name.
    /// </summary>
    public interface IBehaviorGizmoFactory
    {
        /// <summary>
        /// The behavior name this factory handles. Must match the value in
        /// <see cref="Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent.BehaviorName"/>.
        /// </summary>
        string BehaviorName { get; }

        /// <summary>
        /// Returns a fresh or pooled gizmo instance ready for initialisation.
        /// Called when an <see cref="Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent"/> arrives.
        /// </summary>
        IStatefulGizmo Rent();

        /// <summary>
        /// Returns an instance to the pool after <see cref="IStatefulGizmo.OnTeardown"/> has
        /// been called by the system. Implementations may pool or discard the instance.
        /// </summary>
        void Return(IStatefulGizmo gizmo);
    }
}

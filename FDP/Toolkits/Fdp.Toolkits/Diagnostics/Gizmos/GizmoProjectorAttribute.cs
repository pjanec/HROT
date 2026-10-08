using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// Marks a class as a stateless gizmo projector and declares the ECS component
    /// types its matching entities must possess.
    /// Consumed by the <c>GizmoRegistrarGenerator</c> Roslyn source generator, which
    /// emits a <c>GizmoRegistrar.RegisterAll</c> method in the annotated assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class GizmoProjectorAttribute : Attribute
    {
        /// <summary>ECS component types that an entity must carry to receive this gizmo.</summary>
        public Type[] RequiredComponents { get; }

        /// <summary>
        /// ⭐ <c>CE-3120</c> (R-227) — the gizmo FAMILY this projector belongs to, or <c>None</c>. A family has a runtime scope
        /// (<see cref="GizmoFamilies"/>: all units, or the selected and pinned ones) and is what a unit is pinned by
        /// (<c>DebugState.Ai</c>). 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
        /// </summary>
        public Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags Family { get; set; }

        /// <param name="requiredComponents">One or more component types (must be registered
        /// with <see cref="Fdp.Core.ComponentTypeRegistry"/> before
        /// <c>StatelessGizmoRegistry.Register</c> is called).</param>
        public GizmoProjectorAttribute(params Type[] requiredComponents)
        {
            RequiredComponents = requiredComponents ?? Array.Empty<Type>();
        }
    }
}

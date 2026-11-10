using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// Marks a class as a stateless gizmo projector and declares the ECS component
    /// types its matching entities must possess.
    /// Consumed by <see cref="GizmoReflectionRegistrar"/>, which every map host runs (ST-031). ⭐ <c>CE-3123</c>: the old
    /// <c>GizmoRegistrarGenerator</c> source generator (per-namespace <c>GizmoRegistrar.RegisterAll</c>, with no callers since
    /// ST-031) is retired. 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
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

        /// <summary>
        /// ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md D-3, R-1049) — the coarsest map scale this gizmo still draws at, in metres per screen pixel; coarser ⇒ the rule
        /// is skipped for the whole frame (checked once per rule). 0 = any zoom (the default: nothing changes until a projector says so).
        /// </summary>
        public float MaxMetresPerPixel { get; set; }

        /// <summary>
        /// ⭐ CE-4388 (D-4) — this gizmo draws for entities OUTSIDE the view (a line from an off-screen unit to somewhere on screen, an area round an off-screen
        /// anchor): it is exempt from the view test and sees every matching entity, as before.
        /// </summary>
        public bool DrawsOffScreen { get; set; }

        /// <param name="requiredComponents">One or more component types (must be registered
        /// with <see cref="Fdp.Core.ComponentTypeRegistry"/> before
        /// <c>StatelessGizmoRegistry.Register</c> is called).</param>
        public GizmoProjectorAttribute(params Type[] requiredComponents)
        {
            RequiredComponents = requiredComponents ?? Array.Empty<Type>();
        }
    }
}

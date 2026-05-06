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

        /// <param name="requiredComponents">One or more component types (must be registered
        /// with <see cref="Fdp.Core.ComponentTypeRegistry"/> before
        /// <c>StatelessGizmoRegistry.Register</c> is called).</param>
        public GizmoProjectorAttribute(params Type[] requiredComponents)
        {
            RequiredComponents = requiredComponents ?? Array.Empty<Type>();
        }
    }
}

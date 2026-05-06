using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A stateless gizmo projector: reads entity components and issues draw calls
    /// directly each frame. No per-entity instance lifecycle (no OnInitialize /
    /// OnTeardown). A single projector instance is reused for every matching entity.
    /// </summary>
    public interface IStatelessGizmo
    {
        /// <summary>
        /// Called once per frame for each entity that matches the registered component
        /// mask and passes visibility and selection checks.
        /// </summary>
        /// <param name="view">Read-only view of the current ECS world.</param>
        /// <param name="entity">The entity to draw debug visualisation for.</param>
        /// <param name="drawBuilder">Target draw builder; thread-safe for concurrent callers.</param>
        void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder);
    }
}

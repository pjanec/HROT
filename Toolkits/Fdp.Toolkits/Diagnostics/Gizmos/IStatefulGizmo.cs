using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A per-entity stateful gizmo instance managed by <c>DataDrivenGizmoSystem</c>.
    /// The system owns the lifecycle: OnInitialize on construction, UpdateAndDraw each frame,
    /// OnTeardown on entity destruction.
    /// </summary>
    public interface IStatefulGizmo
    {
        void OnInitialize(ISimulationView view, Entity entity);

        void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime,
                           IDebugDrawBuilder drawBuilder);

        void OnTeardown();
    }
}

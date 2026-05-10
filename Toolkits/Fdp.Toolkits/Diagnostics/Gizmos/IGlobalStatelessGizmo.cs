using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A stateless gizmo projector that draws once per frame with no entity context.
    /// Suitable for overlay elements (rubber-band selection, HUD widgets, etc.) that
    /// are not tied to any particular ECS entity.
    /// </summary>
    public interface IGlobalStatelessGizmo
    {
        /// <summary>
        /// Called once per frame. Draw debug primitives via <paramref name="drawBuilder"/>.
        /// </summary>
        void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder);
    }
}

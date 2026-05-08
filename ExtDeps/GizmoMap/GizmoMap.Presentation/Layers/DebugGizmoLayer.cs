using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Standalone Raylib rendering component that wires a <see cref="DebugPrimitiveBuffer"/>
    /// to a <see cref="DebugPrimitiveRenderer2D"/>.
    ///
    /// Adapted from Fdp.Presentation DebugGizmoLayer with the following differences:
    /// - No ISimulationView or FdpEventBus parameters.
    /// - No IMapLayer interface (lives in Fdp.Toolkit.Vis2D.Abstractions).
    /// - Simple constructor taking only buffer and renderer.
    /// </summary>
    public sealed class DebugGizmoLayer
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly DebugPrimitiveRenderer2D _renderer;

        public DebugGizmoLayer(DebugPrimitiveBuffer buffer, DebugPrimitiveRenderer2D renderer)
        {
            _buffer   = buffer;
            _renderer = renderer;
        }

        public void Render(Camera2D camera, float zoom)
        {
            _renderer.Render(_buffer.GetFrame(), camera, zoom);
        }
    }
}

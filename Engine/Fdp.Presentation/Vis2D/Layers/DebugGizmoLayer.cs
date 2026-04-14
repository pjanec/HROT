using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
//using FDP.Toolkit.Vis2D.Debug;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Layers
{
    public class DebugGizmoLayer : IMapLayer
    {
        public string Name => "Debug Gizmos";
        public int LayerBitIndex { get; private set; }

        public DebugGizmoLayer(int layerBitIndex = 31) // Default to bit 31 (top)
        {
            LayerBitIndex = layerBitIndex;
        }

        public void Update(float dt)
        {
            // No update
        }

        public void Draw(RenderContext ctx)
        {
            // Check visibility (unless we treat debug as special?)
            // Usually debug layer is togglable.
            uint maskBit = 1u << LayerBitIndex;
            if ((ctx.VisibleLayersMask & maskBit) == 0)
                return;

            // Debug gizmos not implemented yet
            //DebugGizmos.Instance.RenderAndClear();
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)
        {
            return false; // Debug layer is pass-through
        }

        public Entity? PickEntity(Vector2 worldPos)
        {
            return null;
        }
    }
}

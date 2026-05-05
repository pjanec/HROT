using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Gizmos;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Layers
{
    public class DebugGizmoLayer : IMapLayer
    {
        public string Name => "Debug Gizmos";
        public int LayerBitIndex { get; private set; }

        private DebugPrimitiveBuffer?   _buffer;
        private DebugPrimitiveRenderer2D? _renderer;
        private FdpEventBus?            _eventBus;

        // Hit radius in world units used by HandleInput pick tests.
        private const float HitRadiusWorld = 5f;

        // ---- Constructors ---------------------------------------------------

        /// <summary>Original no-buffer constructor; layer renders nothing until wired.</summary>
        public DebugGizmoLayer(int layerBitIndex = 31)
        {
            LayerBitIndex = layerBitIndex;
        }

        /// <summary>
        /// Production constructor: creates an internal renderer backed by
        /// <paramref name="view"/> for EntityLocal resolution.
        /// </summary>
        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            ISimulationView? view = null)
        {
            LayerBitIndex = layerBitIndex;
            _buffer   = buffer;
            _eventBus = eventBus;
            _renderer = new DebugPrimitiveRenderer2D(view);
        }

        /// <summary>
        /// Test constructor: accepts an externally supplied renderer so that a
        /// <c>CapturingRenderer2D</c> can be injected without Raylib calls.
        /// </summary>
        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            DebugPrimitiveRenderer2D renderer)
        {
            LayerBitIndex = layerBitIndex;
            _buffer   = buffer;
            _eventBus = eventBus;
            _renderer = renderer;
        }

        // ---- IMapLayer ------------------------------------------------------

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            uint maskBit = 1u << LayerBitIndex;
            if ((ctx.VisibleLayersMask & maskBit) == 0) return;

            if (_buffer != null && _renderer != null)
            {
                var primitives = _buffer.GetFrame();
                _renderer.Render(primitives, ctx);
            }
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)
        {
            if (_buffer == null || _eventBus == null) return false;
            if (!isPressed || button != MouseButton.Left) return false;

            var primitives = _buffer.GetFrame();
            DebugPrimitive? best     = null;
            float           bestDist = float.MaxValue;

            foreach (ref readonly var prim in primitives)
            {
                if (!prim.Token.IsValid) continue;

                var primPos = GetPrimitive2DPos(in prim);
                float dist = Vector2.Distance(worldPos, primPos);

                // Prefer topmost layer (highest DebugLayer) when within radius.
                if (dist < HitRadiusWorld
                    && (dist < bestDist || prim.DebugLayer > (best?.DebugLayer ?? 0)))
                {
                    best     = prim;
                    bestDist = dist;
                }
            }

            if (best.HasValue)
            {
                // DEVIATION: canvas is not accessible from a layer, so GizmoInteractionProxyTool
                // cannot be pushed here. Only the event is published; the caller that listens for
                // GizmoInteractionStartedEvent is responsible for pushing the proxy tool.
                _eventBus.Publish(new GizmoInteractionStartedEvent
                {
                    Token    = best.Value.Token,
                    WorldPos = new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f),
                });
                return true;
            }

            return false;
        }

        public Entity? PickEntity(Vector2 worldPos) => null;

        // ---- Private helpers ------------------------------------------------

        private static Vector2 GetPrimitive2DPos(in DebugPrimitive prim) =>
            prim.Shape switch
            {
                DebugPrimitiveShape.Sphere => new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y),
                _                          => new Vector2(prim.LineStart.X,    prim.LineStart.Y),
            };
    }
}

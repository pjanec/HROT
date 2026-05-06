using System;
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
        private MapCanvas?              _canvas;

        // Captures the last RenderContext so HandleInput can use zoom and camera.
        internal RenderContext _lastCtx;

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
            MapCanvas? canvas = null,
            ISimulationView? view = null)
        {
            LayerBitIndex = layerBitIndex;
            _buffer   = buffer;
            _eventBus = eventBus;
            _canvas   = canvas;
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

        /// <summary>
        /// Test constructor: accepts both a canvas (for tool-push verification) and an
        /// externally supplied renderer (to avoid Raylib calls in headless tests).
        /// </summary>
        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            MapCanvas canvas,
            DebugPrimitiveRenderer2D renderer)
        {
            LayerBitIndex = layerBitIndex;
            _buffer   = buffer;
            _eventBus = eventBus;
            _canvas   = canvas;
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

            _lastCtx = ctx;
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)
        {
            if (_buffer == null || _eventBus == null) return false;
            if (!isPressed || button != MouseButton.Left) return false;

            var primitives = _buffer.GetFrame();
            DebugPrimitive? best = null;

            foreach (ref readonly var prim in primitives)
            {
                if (!prim.Token.IsValid) continue;

                if (!HitTest(in prim, worldPos, HitRadiusWorld)) continue;

                if (best == null || prim.DebugLayer > best.Value.DebugLayer)
                    best = prim;
            }

            if (best.HasValue)
            {
                var worldPos3 = new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f);
                if (_canvas != null)
                {
                    var proxy = new GizmoInteractionProxyTool(best.Value.Token, _eventBus!, worldPos3);
                    _canvas.PushTool(proxy);
                    // GizmoInteractionStartedEvent is published in proxy.OnEnter.
                }
                else
                {
                    // Fallback: no canvas (unit test or stub setup); publish directly.
                    _eventBus!.Publish(new GizmoInteractionStartedEvent
                    {
                        Token    = best.Value.Token,
                        WorldPos = worldPos3,
                    });
                }
                return true;
            }

            return false;
        }

        public Entity? PickEntity(Vector2 worldPos) => null;

        // ---- Private helpers ------------------------------------------------

        private bool HitTest(in DebugPrimitive prim, Vector2 testPos, float hitRadius)
        {
            // SizeMode.ScreenPixels primitives are rendered at fixed screen size; scale hit radius.
            float zoom = _lastCtx.Zoom > 0f ? _lastCtx.Zoom : 1f;
            float effectiveRadius = prim.SizeMode == SizeMode.ScreenPixels
                ? hitRadius / zoom
                : hitRadius;

            Vector2 checkPos = testPos;

            // Screen-space primitives use screen-pixel coordinates; convert world pos first.
            if (prim.Space == CoordinateSpace.Screen)
                checkPos = Raylib.GetWorldToScreen2D(testPos, _lastCtx.Camera);

            switch (prim.Shape)
            {
                case DebugPrimitiveShape.Sphere:
                {
                    var center = new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y);
                    return Vector2.Distance(checkPos, center) <= prim.SphereRadius + effectiveRadius;
                }
                case DebugPrimitiveShape.Line:
                {
                    var p0 = new Vector2(prim.LineStart.X, prim.LineStart.Y);
                    var p1 = new Vector2(prim.LineEnd.X,   prim.LineEnd.Y);
                    return PointToSegmentDistance(checkPos, p0, p1) <= effectiveRadius;
                }
                case DebugPrimitiveShape.Arrow:
                {
                    var p0 = new Vector2(prim.ArrowFrom.X, prim.ArrowFrom.Y);
                    var p1 = new Vector2(prim.ArrowTo.X,   prim.ArrowTo.Y);
                    return PointToSegmentDistance(checkPos, p0, p1) <= effectiveRadius;
                }
                case DebugPrimitiveShape.Box2D:
                {
                    var center = new Vector2(prim.BoxCenterX, prim.BoxCenterY);
                    return Vector2.Distance(checkPos, center)
                        <= effectiveRadius + MathF.Max(prim.BoxExtentX, prim.BoxExtentY);
                }
                default:
                {
                    // Text, Icon, EntityBadge: AABB around the anchor position.
                    float tx = prim.Shape == DebugPrimitiveShape.Text
                        ? prim.TextX
                        : prim.LineStart.X;
                    float ty = prim.Shape == DebugPrimitiveShape.Text
                        ? prim.TextY
                        : prim.LineStart.Y;
                    return Vector2.Distance(checkPos, new Vector2(tx, ty)) <= effectiveRadius;
                }
            }
        }

        private static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < float.Epsilon) return Vector2.Distance(p, a);
            float t = MathF.Max(0f, MathF.Min(1f, Vector2.Dot(p - a, ab) / lenSq));
            var closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }
    }
}

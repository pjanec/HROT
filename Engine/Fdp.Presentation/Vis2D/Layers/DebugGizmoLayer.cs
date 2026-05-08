using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Gizmos;
using ContextMenuAdapter = GizmoMap.Presentation.ContextMenuAdapter;

namespace Fdp.Toolkit.Vis2D.Layers
{
    public class DebugGizmoLayer : IMapLayer
    {
        public string Name => "Debug Gizmos";
        public int LayerBitIndex { get; private set; }

        private readonly DebugPrimitiveBuffer? _buffer;
        private readonly DebugPrimitiveRenderer2D? _renderer;
        private readonly FdpEventBus? _eventBus;
        private readonly MapCanvas? _canvas;

        // Context menu presenter (ImGui popup).
        private readonly ContextMenuAdapter _contextMenuAdapter = new();

        internal RenderContext _lastCtx;
        private const float HitRadiusWorld = 5f;

        public DebugGizmoLayer(int layerBitIndex = 31)
        {
            LayerBitIndex = layerBitIndex;
        }

        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            DebugPrimitiveRenderer2D? renderer = null)
        {
            LayerBitIndex = layerBitIndex;
            _buffer = buffer;
            _eventBus = eventBus;
            _renderer = renderer ?? new DebugPrimitiveRenderer2D();
        }

        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            MapCanvas? canvas,
            DebugPrimitiveRenderer2D? renderer = null)
        {
            LayerBitIndex = layerBitIndex;
            _buffer = buffer;
            _eventBus = eventBus;
            _canvas = canvas;
            _renderer = renderer ?? new DebugPrimitiveRenderer2D();
        }

        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            MapCanvas? canvas,
            Fdp.ModuleHost.Abstractions.ISimulationView? view)
        {
            LayerBitIndex = layerBitIndex;
            _buffer = buffer;
            _eventBus = eventBus;
            _canvas = canvas;
            _renderer = new DebugPrimitiveRenderer2D(view);
        }

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            _lastCtx = ctx;
            if (_buffer == null || _renderer == null) return;

            if (LayerBitIndex >= 0 && LayerBitIndex < 32)
            {
                if ((ctx.VisibleLayersMask & (1u << LayerBitIndex)) == 0) return;
            }

            _renderer.SetLayerMask((ushort)ctx.VisibleLayersMask);
            _renderer.Render(_buffer.GetFrame(), ctx);
        }

        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed)
        {
            if (!isPressed || _buffer == null || _eventBus == null) return false;

            if (LayerBitIndex >= 0 && LayerBitIndex < 32)
            {
                if ((_lastCtx.VisibleLayersMask & (1u << LayerBitIndex)) == 0) return false;
            }

            if (button == MapMouseButton.Right)
            {
                return HandleRightClick(worldPos);
            }

            if (button != MapMouseButton.Left) return false;

            var primitives = _buffer.GetFrame();
            DebugPrimitive? best = null;

            foreach (ref readonly var prim in primitives)
            {
                if (!prim.GetPickToken().IsValid) continue;
                if (!HitTest(in prim, worldPos, HitRadiusWorld)) continue;

                if (best == null || prim.DebugLayer > best.Value.DebugLayer)
                {
                    best = prim;
                }
            }

            if (best.HasValue)
            {
                if (_canvas != null)
                {
                    var tool = new GizmoInteractionProxyTool(best.Value.GetPickToken(), _eventBus, _canvas, best.Value.Space);
                    _canvas.PushTool(tool);
                }
                else
                {
                    _eventBus.Publish(new GizmoInteractionStartedEvent
                    {
                        Token = best.Value.GetPickToken(),
                        WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                    });
                }
                return true;
            }

            return false;
        }

        private bool HandleRightClick(Vector2 worldPos)
        {
            var frame = _buffer!.GetFrame();

            // Build a transient map from network entity ID to menu JSON hash from
            // ContextMenuBinding meta-primitives emitted by ContextMenuProjectorGizmo.
            var menuBindings = new Dictionary<long, uint>();
            foreach (ref readonly var prim in frame)
            {
                if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
                    menuBindings[prim.InspNetworkId] = prim.StringHash;
            }

            // Hit-test Box2D primitives and schedule the context menu when the entity
            // has a registered menu binding.
            foreach (ref readonly var prim in frame)
            {
                if (prim.Shape != DebugPrimitiveShape.Box2D) continue;
                if (prim.SubElementId == 0) continue;
                if (!HitTest(in prim, worldPos, HitRadiusWorld)) continue;

                long entityId = prim.InspNetworkId;
                if (entityId == 0) continue;

                if (menuBindings.TryGetValue(entityId, out uint menuHash))
                {
                    string? json = _buffer.InternMap.TryResolve(menuHash);
                    if (json != null)
                    {
                        _contextMenuAdapter.Schedule(entityId, json);
                        return true;
                    }
                    // JSON not yet in InternMap (first frame before StringInternBatch
                    // arrives). Silently ignore; the menu appears on the next right-click.
                }
                break;
            }

            return false;
        }

        /// <summary>
        /// Renders any pending context-menu popup via ImGui and publishes a
        /// <see cref="GizmoMenuActionEvent"/> when the operator selects an item.
        /// Must be called inside an ImGui Begin/End block each frame.
        /// </summary>
        public void DrawContextMenu()
        {
            _contextMenuAdapter.DrawScheduled((anchorId, actionId) =>
                _eventBus?.Publish(new GizmoMenuActionEvent
                {
                    AnchorId = anchorId,
                    ActionId = actionId,
                }));
        }

        public Entity? PickEntity(Vector2 worldPos) => null;

        private bool HitTest(in DebugPrimitive prim, Vector2 testPos, float hitRadius)
        {
            float zoom = _lastCtx.Zoom > 0f ? _lastCtx.Zoom : 1f;
            float effectiveRadius = prim.SizeMode == SizeMode.ScreenPixels ? hitRadius / zoom : hitRadius;

            switch (prim.Shape)
            {
                case DebugPrimitiveShape.Sphere:
                    return Vector2.Distance(testPos, new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y)) <= prim.SphereRadius + effectiveRadius;
                case DebugPrimitiveShape.Line:
                case DebugPrimitiveShape.Arrow:
                    var p0 = prim.Shape == DebugPrimitiveShape.Arrow ? new Vector2(prim.ArrowFrom.X, prim.ArrowFrom.Y) : new Vector2(prim.LineStart.X, prim.LineStart.Y);
                    var p1 = prim.Shape == DebugPrimitiveShape.Arrow ? new Vector2(prim.ArrowTo.X, prim.ArrowTo.Y) : new Vector2(prim.LineEnd.X, prim.LineEnd.Y);
                    return PointToSegmentDistance(testPos, p0, p1) <= effectiveRadius;
                case DebugPrimitiveShape.Box2D:
                case DebugPrimitiveShape.EntityBadge:
                    float dx = Math.Abs(testPos.X - prim.BoxCenterX);
                    float dy = Math.Abs(testPos.Y - prim.BoxCenterY);
                    return dx <= prim.BoxExtentX + effectiveRadius && dy <= prim.BoxExtentY + effectiveRadius;
                default:
                    return Vector2.Distance(testPos, new Vector2(prim.TextX, prim.TextY)) <= effectiveRadius;
            }
        }

        private static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < float.Epsilon) return Vector2.Distance(p, a);
            float t = Math.Max(0f, Math.Min(1f, Vector2.Dot(p - a, ab) / lenSq));
            var closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }
    }
}

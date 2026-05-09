using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Gizmos;
using ContextMenuAdapter = GizmoMap.Presentation.ContextMenuAdapter;
using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using GizmoKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;

namespace Fdp.Toolkit.Vis2D.Layers
{
    public class DebugGizmoLayer : IMapLayer
    {
        public string Name => "Debug Gizmos";
        public int LayerBitIndex { get; private set; }

        private readonly DebugPrimitiveBuffer? _buffer;
        private readonly DebugPrimitiveRenderer2D? _renderer;
        private readonly FdpEventBus? _eventBus;

        // Context menu presenter (ImGui popup).
        private readonly ContextMenuAdapter _contextMenuAdapter = new();

        internal RenderContext _lastCtx;
        private const float HitRadiusWorld = 5f;

        // True while an exclusive InputCaptureBinding is present in the buffer.
        private bool _captureActive;

        // Active gizmo-interaction state (inlined from the old GizmoInteractionProxyTool).
        private PickToken _interactionToken;
        private CoordinateSpace _interactionSpace;
        private bool _interactionDragActive;
        private Vector2 _lastHoverPos = new(float.NaN, float.NaN);

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
            Fdp.ModuleHost.Abstractions.ISimulationView? view)
        {
            LayerBitIndex = layerBitIndex;
            _buffer = buffer;
            _eventBus = eventBus;
            _renderer = new DebugPrimitiveRenderer2D(view);
        }

        public void Update(float dt)
        {
            if (_buffer == null || _eventBus == null) return;

            var frame = _buffer.GetFrame();
            bool hasExclusiveCapture = false;
            for (int i = 0; i < frame.Length; i++)
            {
                ref readonly var prim = ref frame[i];
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding && prim.ConditionMask == 1u)
                {
                    hasExclusiveCapture = true;
                    break;
                }
            }

            _captureActive = hasExclusiveCapture;
        }

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
            if (_buffer == null || _eventBus == null) return false;

            // Capture mode: consume press/release and publish mouse events to the gizmo bus.
            if (_captureActive)
            {
                if (!isPressed && button == MapMouseButton.Left)
                {
                    _eventBus.Publish(new GizmoMouseEvent
                    {
                        Token     = default,
                        Button    = (GizmoMouseButton)(int)button,
                        IsPressed = false,
                        WorldPos  = new Vector3(worldPos.X, worldPos.Y, 0f),
                    });
                }
                else if (!isPressed && button == MapMouseButton.Right)
                {
                    // Right release = cancel signal (published as IsPressed=true by convention).
                    _eventBus.Publish(new GizmoMouseEvent
                    {
                        Token     = default,
                        Button    = (GizmoMouseButton)(int)button,
                        IsPressed = true,
                        WorldPos  = new Vector3(worldPos.X, worldPos.Y, 0f),
                    });
                }
                return true; // Consume all mouse events during exclusive capture.
            }

            // Interaction mode: handle release events for commit/cancel.
            if (!isPressed && _interactionToken.IsValid)
            {
                if (button == MapMouseButton.Left)
                {
                    if (_interactionDragActive)
                    {
                        _interactionDragActive = false;
                        var token = _interactionToken;
                        var space = _interactionSpace;
                        _interactionToken = default;
                        _eventBus.Publish(new GizmoInteractionCommitEvent
                        {
                            Token    = token,
                            WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                            Space    = space,
                        });
                    }
                    else
                    {
                        var token = _interactionToken;
                        _interactionToken = default;
                        _eventBus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    }
                    return true;
                }
                if (button == MapMouseButton.Right)
                {
                    var token = _interactionToken;
                    _interactionToken    = default;
                    _interactionDragActive = false;
                    _eventBus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    return true;
                }
                return false;
            }

            // Only press events are processed below this point.
            if (!isPressed) return false;

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
                _interactionToken    = best.Value.GetPickToken();
                _interactionSpace    = best.Value.Space;
                _interactionDragActive = false;
                _eventBus.Publish(new GizmoInteractionStartedEvent
                {
                    Token    = _interactionToken,
                    WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                });
                return true;
            }

            return false;
        }

        public void HandleHover(Vector2 mouseWorldPos)
        {
            if (_eventBus == null) return;
            if (Vector2.DistanceSquared(_lastHoverPos, mouseWorldPos) < 0.0001f) return;
            _lastHoverPos = mouseWorldPos;
            if (_captureActive || _interactionToken.IsValid)
            {
                _eventBus.Publish(new GizmoDragUpdateEvent
                {
                    Token    = _interactionToken.IsValid ? _interactionToken : default,
                    WorldPos = new Vector3(mouseWorldPos.X, mouseWorldPos.Y, 0f),
                    Space    = _interactionToken.IsValid ? _interactionSpace : CoordinateSpace.World,
                });
            }
        }

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            if (_interactionToken.IsValid)
            {
                _interactionDragActive = true;
                return true; // Consume drag to prevent camera panning during interaction.
            }
            return false;
            // Capture mode does NOT consume drag; camera can still pan/zoom.
        }

        public bool HandleKeyInput(MapKeyboardKey key)
        {
            if (_captureActive)
            {
                _eventBus?.Publish(new GizmoKeyEvent
                {
                    Token     = default,
                    Key       = (GizmoKeyboardKey)(int)key,
                    IsPressed = true,
                });
                return true;
            }
            if (_interactionToken.IsValid && key == MapKeyboardKey.Escape)
            {
                var token = _interactionToken;
                _interactionToken    = default;
                _interactionDragActive = false;
                _eventBus?.Publish(new GizmoInteractionCancelEvent { Token = token });
                return true;
            }
            return false;
        }

        // Test hooks for unit tests.
        internal bool TestHook_IsCaptureActive    => _captureActive;
        internal bool TestHook_IsInteractionActive => _interactionToken.IsValid;

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

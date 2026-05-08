using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    public sealed class GizmoInteractionProxyTool : IMapTool
    {
        public string Name => "GizmoInteractionProxy";

        private readonly PickToken _token;
        private readonly FdpEventBus _eventBus;
        private readonly CoordinateSpace _space;
        private MapCanvas? _canvas;
        private bool _dragActive;

        public GizmoInteractionProxyTool(
            PickToken token,
            FdpEventBus eventBus,
            MapCanvas? canvas = null,
            CoordinateSpace space = CoordinateSpace.World)
        {
            _token = token;
            _eventBus = eventBus;
            _canvas = canvas;
            _space = space;
        }

        public void OnEnter(MapCanvas canvas)
        {
            _canvas = canvas;
            _eventBus.Publish(new GizmoInteractionStartedEvent
            {
                Token = _token,
                WorldPos = Vector3.Zero,
            });
        }

        public void OnExit() => _canvas = null;
        public void Update(float dt) { }
        public void Draw(RenderContext ctx) { }

        public bool HandleHover(Vector2 worldPos) => true;

        public bool HandlePress(Vector2 worldPos, MapMouseButton button)
        {
            if (button == MapMouseButton.Left)
            {
                _dragActive = true;
                return true;
            }
            return false;
        }

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            if (!_dragActive) return false;
            _eventBus.Publish(new GizmoDragUpdateEvent
            {
                Token = _token,
                WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                Space = _space
            });
            return true;
        }

        public bool HandleClick(Vector2 worldPos, MapMouseButton button)
        {
            if (button == MapMouseButton.Left)
            {
                if (_dragActive)
                {
                    _dragActive = false;
                    _eventBus.Publish(new GizmoInteractionCommitEvent
                    {
                        Token = _token,
                        WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                        Space = _space
                    });
                    _canvas?.PopTool();
                    return true;
                }

                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas?.PopTool();
                return false;
            }
            else if (button == MapMouseButton.Right)
            {
                _dragActive = false;
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas?.PopTool();
                return true;
            }
            return false;
        }

        public bool HandleKeyPressed(MapKeyboardKey key)
        {
            if (key == MapKeyboardKey.Escape)
            {
                _dragActive = false;
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}

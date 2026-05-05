using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    public sealed class GizmoInteractionProxyTool : IMapTool
    {
        public string Name => "GizmoInteractionProxy";

        private readonly PickToken _token;
        private readonly FdpEventBus _eventBus;
        private MapCanvas? _canvas;

        public GizmoInteractionProxyTool(PickToken token, FdpEventBus eventBus)
        {
            _token    = token;
            _eventBus = eventBus;
        }

        public void OnEnter(MapCanvas canvas)  => _canvas = canvas;
        public void OnExit()                   => _canvas = null;
        public void Update(float dt)           { }
        public void Draw(RenderContext ctx)    { }

        public bool HandleHover(Vector2 worldPos) => true;

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            _eventBus.Publish(new GizmoDragUpdateEvent
            {
                Token    = _token,
                WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
            });
            return true;
        }

        public bool HandleClick(Vector2 worldPos, MouseButton button)
        {
            // Left button released = commit
            if (button == MouseButton.Left)
            {
                _eventBus.Publish(new GizmoInteractionCommitEvent
                {
                    Token    = _token,
                    WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                });
                _canvas?.PopTool();
                return true;
            }

            // Right button = cancel
            if (button == MouseButton.Right)
            {
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas?.PopTool();
                return true;
            }

            return false;
        }

        public bool HandleKeyPressed(KeyboardKey key)
        {
            if (key == KeyboardKey.Escape)
            {
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}

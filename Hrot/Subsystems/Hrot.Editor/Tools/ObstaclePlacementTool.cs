using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.Editor.Tools
{
    /// <summary>
    /// Map tool that records a single left-click world position and fires
    /// <see cref="OnObstaclePlaced"/> with that position before popping itself.
    /// Used by <see cref="Adapters.EditorZoneAdapter"/> to turn a click into a
    /// <see cref="Hrot.Map.Common.Events.SpawnZoneObstacleCommand"/>.
    /// </summary>
    public sealed class ObstaclePlacementTool : IMapTool
    {
        /// <inheritdoc/>
        public string Name => "ObstaclePlacement";

        /// <summary>Raised immediately before the tool pops itself.</summary>
        public Action<Vector2>? OnObstaclePlaced;

        private readonly float _radius;
        private MapCanvas? _canvas;
        private Vector2 _currentMousePos;

        /// <param name="radius">Preview radius indicator drawn at the cursor.</param>
        /// <param name="onPlaced">
        /// Callback fired with the clicked world position when the operator left-clicks.
        /// </param>
        public ObstaclePlacementTool(float radius, Action<Vector2>? onPlaced = null)
        {
            _radius = radius;
            if (onPlaced != null)
                OnObstaclePlaced += onPlaced;
        }

        /// <inheritdoc/>
        public void OnEnter(MapCanvas canvas) => _canvas = canvas;

        /// <inheritdoc/>
        public void OnExit() => _canvas = null;

        /// <inheritdoc/>
        public void Update(float dt) { }

        /// <inheritdoc/>
        public void Draw(RenderContext ctx)
        {
            if (_canvas != null)
                ctx.DrawBuilder?.DrawSphere(
                    new System.Numerics.Vector3(_currentMousePos.X, _currentMousePos.Y, 0f),
                    _radius,
                    Rgba32.Red);
        }

        /// <inheritdoc/>
        public bool HandleClick(Vector2 worldPos, MapMouseButton button)
        {
            if (button == MapMouseButton.Left)
            {
                OnObstaclePlaced?.Invoke(worldPos);
                _canvas?.PopTool();
                return true;
            }
            if (button == MapMouseButton.Right)
            {
                _canvas?.PopTool();
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

        /// <inheritdoc/>
        public bool HandleHover(Vector2 worldPos)
        {
            _currentMousePos = worldPos;
            return false;
        }

        /// <inheritdoc/>
        public bool HandleKeyPressed(MapKeyboardKey key)
        {
            if (key == MapKeyboardKey.Escape)
            {
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}

using System;
using System.Numerics;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

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
        public void Draw(RenderContext ctx) { }

        /// <inheritdoc/>
        public bool HandleClick(Vector2 worldPos, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                OnObstaclePlaced?.Invoke(worldPos);
                _canvas?.PopTool();
                return true;
            }
            if (button == MouseButton.Right)
            {
                _canvas?.PopTool();
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

        /// <inheritdoc/>
        public bool HandleHover(Vector2 worldPos) => false;

        /// <inheritdoc/>
        public bool HandleKeyPressed(KeyboardKey key)
        {
            if (key == KeyboardKey.Escape)
            {
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}

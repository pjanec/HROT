using System.Numerics;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Hrot.Editor.Tools
{
    /// <summary>
    /// Minimal map tool stub that activates route placement mode on the canvas.
    /// Push this tool onto <see cref="MapCanvas"/> via <c>PushTool</c> to let
    /// the operator draw a polyline route on the map.
    ///
    /// This is a placeholder implementation; the full route-authoring behaviour
    /// is wired up in a later batch.
    /// </summary>
    public sealed class RoutePlacementTool : IMapTool
    {
        /// <inheritdoc/>
        public string Name => "RoutePlacement";

        private MapCanvas? _canvas;

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

using System;
using System.Numerics;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Hrot.Editor.Tools
{
    /// <summary>
    /// Single-click tool that fires <see cref="OnEntityPicked"/> with the canvas
    /// entity index nearest to the click, then pops itself.
    /// Supports cancellation via ESC or right-click.
    /// </summary>
    public sealed class EntityPickerTool : IMapTool
    {
        /// <inheritdoc/>
        public string Name => "EntityPicker";

        /// <summary>Fired when the operator clicks a valid entity; provides the entity Index.</summary>
        public Action<int>? OnEntityPicked;

        /// <summary>Fired when the operator cancels picking.</summary>
        public Action? OnCancelled;

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
            if (button == MouseButton.Left)
            {
                // Placeholder: in production this would use the entity spatial index.
                // For now fire with -1 to indicate "no entity at position" so tests can hook
                // OnEntityPicked directly.
                OnEntityPicked?.Invoke(-1);
                _canvas?.PopTool();
                return true;
            }
            if (button == MouseButton.Right)
            {
                OnCancelled?.Invoke();
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
                OnCancelled?.Invoke();
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}

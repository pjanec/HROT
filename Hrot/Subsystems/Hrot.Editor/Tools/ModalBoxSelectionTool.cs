using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Hrot.Editor.Tools
{
    /// <summary>
    /// Rubber-band selection tool that tracks a drag rectangle on the map canvas and
    /// fires <see cref="OnSelectionComplete"/> on mouse-button-up with the list of
    /// entity indices inside the selection rectangle.
    ///
    /// <para>Current implementation fires the callback immediately on left-click with
    /// an empty list (placeholder); the full box-query over the entity spatial index
    /// is wired up in a later batch.</para>
    /// </summary>
    public sealed class ModalBoxSelectionTool : IMapTool
    {
        /// <inheritdoc/>
        public string Name => "ModalBoxSelection";

        /// <summary>
        /// Fired when the operator releases the mouse button after drawing a selection box.
        /// The parameter is the list of entity indices within the selection bounds.
        /// </summary>
        public Action<IReadOnlyList<int>>? OnSelectionComplete;

        /// <summary>Fired when the operator cancels the selection.</summary>
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
                // Placeholder: fires with empty list; spatial query wired in later batch.
                OnSelectionComplete?.Invoke(Array.Empty<int>());
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

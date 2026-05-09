using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using GizmoKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// Thin canvas bridge that translates raw <see cref="IMapTool"/> events into
    /// <see cref="IEntityStatefulGizmo"/> method calls for placement gizmos.
    ///
    /// Used as a temporary input bridge until Phase 6 of the eradication migrates
    /// all input routing to the ECS pipeline.
    ///
    /// Lifecycle: pushed by the caller via <c>canvas.PushTool(bridge)</c>. When the
    /// wrapped gizmo's <c>onRemove</c> delegate fires (committed or cancelled),
    /// <see cref="RequestPop"/> is called, which pops this bridge off the canvas and
    /// triggers <see cref="OnExit"/> which disposes the gizmo.
    /// </summary>
    public sealed class PlacementCanvasBridge : IMapTool
    {
        /// <inheritdoc/>
        public string Name => "PlacementBridge";

        private readonly IEntityStatefulGizmo _gizmo;
        private MapCanvas? _canvas;

        /// <param name="gizmo">The stateful gizmo that handles placement logic and drawing.</param>
        public PlacementCanvasBridge(IEntityStatefulGizmo gizmo)
        {
            _gizmo = gizmo;
        }

        // IMapTool lifecycle

        /// <inheritdoc/>
        public void OnEnter(MapCanvas canvas)
        {
            _canvas = canvas;
            _gizmo.SetFocus(true);
        }

        /// <inheritdoc/>
        public void OnExit()
        {
            _gizmo.SetFocus(false);
            _gizmo.Dispose();
            _canvas = null;
        }

        /// <inheritdoc/>
        public void Update(float dt) { }

        /// <inheritdoc/>
        public void Draw(RenderContext ctx)
        {
            if (ctx.DrawBuilder != null)
                _gizmo.UpdateAndDraw(0f, ctx.DrawBuilder);
        }

        // IMapTool input

        /// <inheritdoc/>
        public bool HandleHover(Vector2 worldPos)
        {
            _gizmo.OnDragUpdate(new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f));
            return true;
        }

        /// <inheritdoc/>
        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            _gizmo.OnDragUpdate(new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f));
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Left = released (commit), isPressed=false.
        /// Right = pressed (cancel), isPressed=true.
        /// Matches the canvas click-release semantics used throughout the tool stack.
        /// </remarks>
        public bool HandleClick(Vector2 worldPos, MapMouseButton button)
        {
            var pos = new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f);
            // Left = released (commit), Right = pressed (cancel) -- matches canvas semantics
            bool isPressed = button != MapMouseButton.Left;
            _gizmo.OnMouseEvent((GizmoMouseButton)(int)button, isPressed, pos);
            return true;
        }

        /// <inheritdoc/>
        public bool HandleKeyPressed(MapKeyboardKey key)
        {
            _gizmo.OnKeyEvent((GizmoKeyboardKey)(int)key, isPressed: true);
            return true;
        }

        /// <summary>
        /// Pops this bridge off the canvas. Called by the gizmo's <c>onRemove</c> delegate
        /// to signal that placement is complete or cancelled.
        /// Triggers <see cref="OnExit"/> which disposes the wrapped gizmo.
        /// </summary>
        public void RequestPop() => _canvas?.PopTool();
    }
}

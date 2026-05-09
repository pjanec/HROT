using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace GizmoMap.Example
{
    // Shared-focus gizmo that lets the operator drag individual vertices of a polygon.
    // Multiple instances (one per polygon) coexist without coordination -- each reacts
    // only to events routed to its own AnchorId by the GizmoInteractionManager.
    //
    // Design (gizmo-input-focus-design.md section 10.1):
    // - RequiresExclusiveFocus = false: spatial hit-testing on the terminal picks the vertex.
    // - Each vertex emits a Box2D handle with SubElementId = vertexIndex + 1.
    //   SubElementId == 0 is reserved for "no handle" and is skipped by the terminal.
    // - The InspNetworkId field on each handle primitive carries the polygon AnchorId so
    //   the terminal builds the correct GizmoPickToken (AnchorId = polygon, SubElementId = vertex).
    // - Started carries the full token; OnInteractionStarted extracts SubElementId to identify
    //   the active vertex.
    public sealed class VertexEditGizmo : IStatefulGizmo
    {
        private readonly long _anchorId;
        private readonly Vector2[] _vertices;

        private int    _activeVertex = -1;
        private Vector2 _savedPos;

        // Color of idle vertex handles.
        private static readonly Rgba32 IdleColor   = new Rgba32(0, 210, 100, 210);
        // Color of the active (dragged) vertex handle.
        private static readonly Rgba32 ActiveColor = Rgba32.Red;
        // Color of polygon edges.
        private static readonly Rgba32 EdgeColor   = new Rgba32(0, 180, 90, 200);

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        // anchorId   - stable ID used to register this gizmo in GizmoInteractionManager.
        //              Every Box2D handle emitted sets InspNetworkId to this value so the
        //              terminal routes Started events here.
        // vertices   - mutable polygon vertices edited in-place during drag interactions.
        public VertexEditGizmo(long anchorId, Vector2[] vertices)
        {
            _anchorId = anchorId;
            _vertices = vertices;
        }

        public void UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw)
        {
            // Draw polygon edges.
            for (int i = 0; i < _vertices.Length; i++)
            {
                int j = (i + 1) % _vertices.Length;
                draw.DrawLine(
                    new Vector3(_vertices[i].X, _vertices[i].Y, 0f),
                    new Vector3(_vertices[j].X, _vertices[j].Y, 0f),
                    EdgeColor, thickness: 1.5f, sizeMode: SizeMode.ScreenPixels);
            }

            // Draw interactive vertex handles (Box2D with SubElementId != 0).
            for (int i = 0; i < _vertices.Length; i++)
            {
                bool active = (_activeVertex == i);
                var prim = default(DebugPrimitive);
                prim.Shape        = DebugPrimitiveShape.Box2D;
                prim.Space        = CoordinateSpace.World;
                prim.TargetView   = PipelineTarget.Map2D;
                prim.BoxCenterX   = _vertices[i].X;
                prim.BoxCenterY   = _vertices[i].Y;
                prim.BoxExtentX   = 8f;
                prim.BoxExtentY   = 8f;
                prim.Color        = active ? ActiveColor : IdleColor;
                prim.SubElementId = (ushort)(i + 1);    // 1-based; 0 is non-interactive
                prim.BoxAnchorId  = _anchorId;           // routes Started to this gizmo's manager slot
                draw.EmitRaw(in prim);
            }
        }

        // token.SubElementId identifies which vertex handle was hit.
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
        {
            int idx = (int)token.SubElementId - 1; // convert 1-based to 0-based
            if ((uint)idx >= (uint)_vertices.Length) return;
            _activeVertex = idx;
            _savedPos     = _vertices[idx];
        }

        public void OnDragUpdate(Vector3 worldPos)
        {
            if (_activeVertex < 0) return;
            _vertices[_activeVertex] = new Vector2(worldPos.X, worldPos.Y);
        }

        public void OnCommit(Vector3 worldPos)
        {
            if (_activeVertex < 0) return;
            _vertices[_activeVertex] = new Vector2(worldPos.X, worldPos.Y);
            _activeVertex = -1;
        }

        public void OnCancel()
        {
            if (_activeVertex < 0) return;
            _vertices[_activeVertex] = _savedPos;
            _activeVertex = -1;
        }

        // Vertex editor does not use menu actions or raw HW events.
        public void OnMenuAction(int actionId)                                          { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)                      { }

        public void Dispose() { }
    }
}

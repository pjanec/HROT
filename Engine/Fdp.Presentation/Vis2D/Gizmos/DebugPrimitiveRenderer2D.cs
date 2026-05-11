using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    public class DebugPrimitiveRenderer2D
    {
        private readonly GizmoMap.Presentation.DebugPrimitiveRenderer2D _inner;

        public DebugPrimitiveRenderer2D(
            ISimulationView? view = null,
            GizmoMap.Presentation.Shapes.IEntityShapeLibrary? shapeLibrary = null,
            GizmoMap.Presentation.ImGuiPropertyTreeAdapter? imGuiAdapter = null)
        {
            _inner = new GizmoMap.Presentation.DebugPrimitiveRenderer2D(
                shapeLibrary ?? new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary(),
                imGuiAdapter);
        }

        public void SetLayerMask(ushort mask) { }

        public void Render(ReadOnlySpan<DebugPrimitive> primitives, RenderContext ctx)
        {
            var zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
            var mapCamera = ctx.Resources.Get<MapCamera>();
            Camera2D camera = mapCamera != null ? mapCamera.InnerCamera : default;

            foreach (ref readonly var prim in primitives)
            {
                DispatchShape(in prim, ctx);
            }

            _inner.Render(primitives, camera, zoom);
        }

        protected virtual void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
        {
        }
    }
}

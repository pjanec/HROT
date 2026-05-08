using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    public class DebugPrimitiveRenderer2D
    {
        private readonly GizmoMap.Presentation.DebugPrimitiveRenderer2D _inner;

        public DebugPrimitiveRenderer2D(ISimulationView? view = null)
        {
            _inner = new GizmoMap.Presentation.DebugPrimitiveRenderer2D();
        }

        public void SetLayerMask(ushort mask) => _inner.SetLayerMask(mask);

        public void Render(ReadOnlySpan<DebugPrimitive> primitives, RenderContext ctx)
        {
            var zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
            foreach (ref readonly var prim in primitives)
            {
                DispatchShape(in prim, ctx);
                _inner.Render(new[] { prim }, default, zoom);
            }
        }

        protected virtual void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
        {
        }
    }
}

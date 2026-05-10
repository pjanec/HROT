using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// Global gizmo that draws the rubber-band selection rectangle while the operator
    /// is dragging over empty map space.
    /// </summary>
    public sealed class RubberBandGizmo : IGlobalStatelessGizmo
    {
        private static readonly Rgba32 FillColor   = new Rgba32(0, 120, 215, 50);
        private static readonly Rgba32 BorderColor = new Rgba32(0, 120, 215, 200);

        private readonly RubberBandState _state;

        public RubberBandGizmo(RubberBandState state)
        {
            _state = state;
        }

        public void Draw(ISimulationView view, IDebugDrawBuilder draw)
        {
            if (!_state.IsActive) return;

            float x0 = _state.Start.X;
            float y0 = _state.Start.Y;
            float x1 = _state.Current.X;
            float y1 = _state.Current.Y;

            float minX = x0 < x1 ? x0 : x1;
            float maxX = x0 > x1 ? x0 : x1;
            float minY = y0 < y1 ? y0 : y1;
            float maxY = y0 > y1 ? y0 : y1;

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float ex = (maxX - minX) * 0.5f;
            float ey = (maxY - minY) * 0.5f;

            // Semi-transparent fill.
            var fill = default(DebugPrimitive);
            fill.Shape      = DebugPrimitiveShape.Box2D;
            fill.Color      = FillColor;
            fill.TargetView = PipelineTarget.Map2D;
            fill.Space      = CoordinateSpace.World;
            fill.BoxCenterX = cx;
            fill.BoxCenterY = cy;
            fill.BoxExtentX = ex;
            fill.BoxExtentY = ey;
            draw.EmitRaw(in fill);

            // Border lines (screen-space thickness 1 px).
            var tl = new Vector3(minX, minY, 0f);
            var tr = new Vector3(maxX, minY, 0f);
            var br = new Vector3(maxX, maxY, 0f);
            var bl = new Vector3(minX, maxY, 0f);

            draw.DrawLine(tl, tr, BorderColor, thickness: 1f, sizeMode: SizeMode.ScreenPixels);
            draw.DrawLine(tr, br, BorderColor, thickness: 1f, sizeMode: SizeMode.ScreenPixels);
            draw.DrawLine(br, bl, BorderColor, thickness: 1f, sizeMode: SizeMode.ScreenPixels);
            draw.DrawLine(bl, tl, BorderColor, thickness: 1f, sizeMode: SizeMode.ScreenPixels);
        }
    }
}

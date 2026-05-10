using System;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Example
{
    /// <summary>
    /// Minimal IGizmoDrawBuilder adapter backed by a GizmoPrimitiveBuffer.
    /// Used by DemoSceneGenerator to emit raw DebugPrimitive values.
    /// </summary>
    public sealed class LocalDrawBuilder : IGizmoDrawBuilder
    {
        private readonly GizmoPrimitiveBuffer _buffer;

        public LocalDrawBuilder(GizmoPrimitiveBuffer buffer)
        {
            _buffer = buffer;
        }

        // Expose the underlying buffer for direct raw primitive emission.
        public GizmoPrimitiveBuffer Buffer => _buffer;

        // ---- IGizmoDrawBuilder -----------------------------------------

        public void DrawLine(
            System.Numerics.Vector3 start, System.Numerics.Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0)
        {
            _buffer.DrawLine(start, end, color, thickness, sizeMode, target, layer);
        }

        public void DrawLineGradient(
            System.Numerics.Vector3 start, System.Numerics.Vector3 end,
            Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0)
        {
            _buffer.DrawLineGradient(start, end, startColor, endColor, thickness, sizeMode, target, layer);
        }

        public void DrawSphere(
            System.Numerics.Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0)
        {
            _buffer.DrawSphere(center, radius, color, thickness, sizeMode, target, layer);
        }

        public void DrawArrow(
            System.Numerics.Vector3 from, System.Numerics.Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0)
        {
            _buffer.DrawArrow(from, to, color, headSize, layer);
        }

        public void DrawText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0)
        {
            _buffer.DrawText(x, y, text, color, space, layer);
        }

        public void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0)
        {
            _buffer.DrawTextLong(x, y, text, color, space, layer);
        }

        /// <summary>
        /// Emits a raw DebugPrimitive directly into the underlying buffer.
        /// Used for shapes not covered by IGizmoDrawBuilder (SpatialAnchor, MilStd2525, etc.)
        /// and for InputCaptureBinding meta-primitives emitted by gizmo managers.
        /// Implements <see cref="IGizmoDrawBuilder.EmitRaw"/>.
        /// </summary>
        public void EmitRaw(in DebugPrimitive prim)
        {
            _buffer.AppendRaw(in prim);
        }
    }
}

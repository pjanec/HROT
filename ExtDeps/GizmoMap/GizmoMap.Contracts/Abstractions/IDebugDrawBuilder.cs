using System.Numerics;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // ECS-free interface for emitting debug primitives into a draw builder.
    // Entity-dependent methods (DrawEntityBadge, DrawEntityLocal, DrawEntityLocalInteractive)
    // are intentionally omitted — they require ECS access and live in Fdp.Diagnostics.Contracts.
    // Named IGizmoDrawBuilder (not IDebugDrawBuilder) to avoid FQN collision with the
    // ECS-extended IDebugDrawBuilder in Fdp.Diagnostics.Contracts.
    public interface IGizmoDrawBuilder
    {
        void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid);

        void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid);

        void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid);

        void DrawBox2D(
            Vector2 center, Vector2 extents, Rgba32 color,
            float angleDeg = 0f,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid,
            long anchorId = 0,
            ushort subElementId = 0)
        {
        }

        void DrawArrow(
            Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0);

        void DrawText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0);

        // Interns full managed string for text exceeding 31 chars; emits StringHash != 0.
        // The first 31 chars are stored inline as a preview fallback.
        // NOTE: allocates on the intern map registration path (cold path only);
        // subsequent calls with the same text hit the map and allocate nothing.
        void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0);

        /// <summary>
        /// Called once per frame before gizmo systems execute. Advances the persistence clock
        /// by <paramref name="deltaTime"/>, evicts expired persistent primitives, clears the
        /// transient buffer, and re-injects surviving persistent primitives.
        /// Default no-op for implementations that do not support persistence.
        /// </summary>
        void EndFrame(float deltaTime) { }

        /// <summary>
        /// Emits a raw <see cref="DebugPrimitive"/> directly into the buffer.
        /// Used for shapes not covered by the typed draw methods (e.g. SpatialAnchor,
        /// InputCaptureBinding, Box2D with SubElementId).
        /// Default no-op for implementations that do not expose raw emission.
        /// </summary>
        void EmitRaw(in DebugPrimitive prim) { }
    }
}

using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IDebugDrawBuilder
    {
        void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0);

        void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0);

        void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0);

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

        void DrawEntityBadge(
            Entity target, FixedString32 richText,
            PipelineTarget targetPipeline = PipelineTarget.All);

        void DrawEntityLocal(
            Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, float thickness = 1f, byte layer = 0);
    }
}

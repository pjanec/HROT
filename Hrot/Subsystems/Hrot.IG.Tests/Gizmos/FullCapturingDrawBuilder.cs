using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.IG.Tests.Gizmos
{
    // Extended capturing draw builder that records all draw call types.
    // Used by EntityRotation, VisibilityCone, and HillAttack gizmo tests.
    internal sealed class FullCapturingDrawBuilder : IDebugDrawBuilder
    {
        public readonly List<(Vector3 From, Vector3 To, Rgba32 Color)> ArrowCalls   = new();
        public readonly List<(float X, float Y, FixedString32 Text, Rgba32 Color)> TextCalls = new();
        public readonly List<(Vector3 Start, Vector3 End, Rgba32 Color)> LineCalls  = new();
        public readonly List<(Vector3 Center, float Radius, Rgba32 Color)> SphereCalls = new();
        public readonly List<(Entity Target, FixedString32 Text)> BadgeCalls        = new();

        public void DrawArrow(Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f, byte layer = 0)
            => ArrowCalls.Add((from, to, color));

        public void DrawText(float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0)
            => TextCalls.Add((x, y, text, color));

        public void DrawLine(Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All, byte layer = 0)
            => LineCalls.Add((start, end, color));

        public void DrawSphere(Vector3 center, float radius, Rgba32 color,
            float thickness = 0f, SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All, byte layer = 0)
            => SphereCalls.Add((center, radius, color));

        public void DrawEntityBadge(Entity entity, FixedString32 richText,
            PipelineTarget targetPipeline = PipelineTarget.All)
            => BadgeCalls.Add((entity, richText));

        // Unused stubs.
        public void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All, byte layer = 0) { }

        public void DrawTextLong(float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0) { }

        public void DrawEntityLocal(Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, float thickness = 1f, byte layer = 0) { }

        public void DrawEntityLocalInteractive(Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, ushort subElementId, float thickness = 1f, byte layer = 0) { }
    }
}

using System;
using System.Numerics;
using System.Threading;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Thread-safe append-only buffer of DebugPrimitive values.
    // Pre-allocated at construction; no per-frame heap allocation on the draw path.
    // When capacity is exhausted, primitives are silently dropped (DroppedCount tracks overflow).
    public sealed class DebugPrimitiveBuffer : IDebugDrawBuilder
    {
        private readonly DebugPrimitive[] _primitives;
        private int _count;
        private int _droppedCount;
        private readonly StringInternMap _internMap;

        // Number of primitives dropped due to capacity overflow.
        public int DroppedCount => _droppedCount;

        // The intern map used by DrawTextLong. Exposed for consumers that resolve long-text hashes.
        public StringInternMap InternMap => _internMap;

        public DebugPrimitiveBuffer(int capacity = 4096, StringInternMap? internMap = null)
        {
            _primitives = new DebugPrimitive[capacity];
            _internMap  = internMap ?? new StringInternMap();
        }

        // Returns a zero-copy span of all primitives written this frame.
        public ReadOnlySpan<DebugPrimitive> GetFrame()
        {
            int count = Math.Min(_count, _primitives.Length);
            return _primitives.AsSpan(0, count);
        }

        // Resets the write cursor for the next frame. Safe to call from the render thread.
        public void Clear()
        {
            _count        = 0;
            _droppedCount = 0;
        }

        // ---- IDebugDrawBuilder implementation --------------------------------

        public void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0)
        {
            Append(DebugPrimitive.MakeLine(start, end, color, thickness, sizeMode, target, layer));
        }

        public void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0)
        {
            var p = DebugPrimitive.MakeLine(start, end, startColor, thickness, sizeMode, target, layer);
            p.EndColor = endColor;
            Append(p);
        }

        public void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0)
        {
            Append(DebugPrimitive.MakeSphere(center, radius, color, target, layer));
        }

        public void DrawArrow(
            Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0)
        {
            Append(DebugPrimitive.MakeArrow(from, to, color, headSize, layer));
        }

        public void DrawText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0)
        {
            // StringHash is always 0 for inline FixedString32 mode.
            Append(DebugPrimitive.MakeText(x, y, text, color, space, layer));
        }

        public void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0)
        {
            uint hash = StringInternMap.Fnv1a32(text);
            _internMap.Intern(hash, text);   // idempotent; allocates only on first call

            var p = default(DebugPrimitive);
            p.Shape      = DebugPrimitiveShape.Text;
            p.Space      = space;
            p.Color      = color;
            p.TargetView = PipelineTarget.All;
            p.DebugLayer = layer;
            p.TextX      = x;
            p.TextY      = y;
            // StringHash overlay at offset 8 (overlapping AnchorIndex)
            p.StringHash  = hash;
            // Store first MaxLength chars inline as a preview; FixedString32 auto-truncates.
            p.TextContent = new FixedString32(text);
            Append(p);
        }

        public void DrawEntityBadge(
            Entity target, FixedString32 richText,
            PipelineTarget targetPipeline = PipelineTarget.All)
        {
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.EntityBadge;
            p.TargetView       = targetPipeline;
            p.BadgeTargetIndex = target.Index;
            p.BadgeTargetGen   = target.Generation;
            p.BadgeRichText    = richText;
            Append(p);
        }

        public void DrawEntityLocal(
            Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, float thickness = 1f, byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Line;
            p.Space            = CoordinateSpace.EntityLocal;
            p.Color            = color;
            p.EndColor         = color;
            p.TargetView       = PipelineTarget.All;
            p.DebugLayer       = layer;
            p.SizeMode         = SizeMode.ScreenPixels;
            p.ThicknessU16     = (ushort)(thickness * 10f);
            p.AnchorIndex      = anchor.Index;
            p.AnchorGeneration = anchor.Generation;
            p.LineStart        = localStart;
            p.LineEnd          = localEnd;
            Append(p);
        }

        // ---- Internal helpers -----------------------------------------------

        private void Append(DebugPrimitive p)
        {
            int slot = Interlocked.Increment(ref _count) - 1;
            if ((uint)slot >= (uint)_primitives.Length)
            {
                Interlocked.Increment(ref _droppedCount);
                return;
            }
            _primitives[slot] = p;
        }
    }
}

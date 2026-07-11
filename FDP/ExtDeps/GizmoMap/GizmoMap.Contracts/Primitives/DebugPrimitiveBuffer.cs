using System;
using System.Numerics;
using System.Threading;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Thread-safe append-only buffer of DebugPrimitive values.
    // Pre-allocated at construction; no per-frame heap allocation on the draw path.
    // When capacity is exhausted, primitives are silently dropped (DroppedCount tracks overflow).
    // Entity-dependent methods (DrawEntityBadge, DrawEntityLocal, DrawEntityLocalInteractive)
    // are omitted in this assembly — they live in Fdp.Diagnostics.Contracts.
    // Named GizmoPrimitiveBuffer (not DebugPrimitiveBuffer) to avoid FQN collision with the
    // ECS-extended DebugPrimitiveBuffer in Fdp.Diagnostics.Contracts.
    public sealed class GizmoPrimitiveBuffer : IGizmoDrawBuilder
    {
        private readonly DebugPrimitive[] _primitives;
        private int _count;
        private int _droppedCount;
        private readonly StringInternMap _internMap;

        // Persistent re-emission: primitives with LifetimeSeconds > 0 survive across frames.
        private readonly DebugPrimitive[] _persistent;
        private readonly float[] _remainingLife;
        private int _persistentCount;
        private const int PersistentCapacity = 256;

        // Number of primitives dropped due to capacity overflow.
        public int DroppedCount => _droppedCount;

        // The intern map used by DrawTextLong. Exposed for consumers that resolve long-text hashes.
        public StringInternMap InternMap => _internMap;

        public GizmoPrimitiveBuffer(int capacity = 4096, StringInternMap? internMap = null)
        {
            _primitives    = new DebugPrimitive[capacity];
            _persistent    = new DebugPrimitive[PersistentCapacity];
            _remainingLife = new float[PersistentCapacity];
            _internMap     = internMap ?? new StringInternMap();
        }

        // Returns a zero-copy span of all primitives written this frame.
        public ReadOnlySpan<DebugPrimitive> GetFrame()
        {
            int count = Math.Min(_count, _primitives.Length);
            return _primitives.AsSpan(0, count);
        }

        // Resets the transient write cursor for the next frame. Persistent entries are NOT affected.
        // For frame-boundary management call EndFrame(deltaTime) instead.
        public void Clear()
        {
            _count        = 0;
            _droppedCount = 0;
        }

        /// <summary>
        /// Appends a primitive directly into the transient buffer without persistence tracking.
        /// Used by network ingress to restore received primitives. Thread-safe (uses Interlocked).
        /// </summary>
        public void AppendRaw(in DebugPrimitive primitive)
        {
            int slot = Interlocked.Increment(ref _count) - 1;
            if ((uint)slot < (uint)_primitives.Length)
                _primitives[slot] = primitive;
            else
                Interlocked.Increment(ref _droppedCount);
        }

        /// <summary>
        /// Advances the persistence clock, evicts expired entries, clears the transient buffer,
        /// and re-injects surviving persistent primitives. Call once per frame BEFORE gizmo
        /// systems execute.
        /// </summary>
        public void EndFrame(float deltaTime)
        {
            // Compact persistent array: keep entries whose remaining life exceeds deltaTime.
            int writeIdx = 0;
            int count = Math.Min(_persistentCount, _persistent.Length);
            for (int i = 0; i < count; i++)
            {
                float newLife = _remainingLife[i] - deltaTime;
                if (newLife > 0f)
                {
                    _persistent[writeIdx]    = _persistent[i];
                    _remainingLife[writeIdx] = newLife;
                    writeIdx++;
                }
            }
            _persistentCount = writeIdx;

            // Reset the transient buffer.
            _count        = 0;
            _droppedCount = 0;

            // Re-inject surviving persistent primitives into the start of the transient buffer.
            for (int i = 0; i < _persistentCount; i++)
            {
                int slot = Interlocked.Increment(ref _count) - 1;
                if ((uint)slot < (uint)_primitives.Length)
                    _primitives[slot] = _persistent[i];
                else
                    Interlocked.Increment(ref _droppedCount);
            }
        }

        // ---- IGizmoDrawBuilder implementation --------------------------------

        public void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid)
        {
            Append(DebugPrimitive.MakeLine(start, end, color, thickness, sizeMode, target, layer, style));
        }

        public void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid)
        {
            var p = DebugPrimitive.MakeLine(start, end, startColor, thickness, sizeMode, target, layer, style);
            p.EndColor = endColor;
            Append(p);
        }

        public void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid)
        {
            Append(DebugPrimitive.MakeSphere(center, radius, color, thickness, sizeMode, target, layer, fillColor, style));
        }

        public void DrawBox2D(
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
            Append(DebugPrimitive.MakeBox2D(center, extents, color, angleDeg, thickness, sizeMode, target, layer, fillColor, style, anchorId, subElementId));
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
            byte layer = 0,
            float fontSizePx = 0f,
            float lineOffsetPx = 0f)
        {
            Append(DebugPrimitive.MakeText(x, y, text, color, space, layer, fontSizePx, lineOffsetPx));
        }

        public void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0,
            float fontSizePx = 0f,
            float lineOffsetPx = 0f)
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
            // Store first MaxLength chars inline as a preview fallback.
            p.TextContent = new FixedString32(text);
            // ThicknessU16 repurposed for Text: carries desired screen-pixel font size (not * 10).
            if (fontSizePx > 0f)
                p.ThicknessU16 = (ushort)fontSizePx;
            // AnchorGeneration carries the screen-pixel line offset for Text primitives (signed).
            if (lineOffsetPx != 0f)
                p.AnchorGeneration = unchecked((ushort)(short)lineOffsetPx);
            Append(p);
        }

        /// <summary>
        /// Emits a raw primitive directly into the transient buffer.
        /// Implements <see cref="IGizmoDrawBuilder.EmitRaw"/>.
        /// </summary>
        public void EmitRaw(in DebugPrimitive prim) => Append(prim);

        /// <summary>
        /// Interns <paramref name="menuJson"/> and emits a <see cref="DebugPrimitiveShape.MainMenuBinding"/>
        /// meta-primitive so the dumb terminal merges it into the global main menu bar.
        /// </summary>
        public void DrawMainMenuBinding(string menuJson)
        {
            uint hash = StringInternMap.Fnv1a32(menuJson);
            _internMap.Intern(hash, menuJson);
            Append(DebugPrimitive.MakeMainMenuBinding(hash));
        }

        // ---- Internal helpers -----------------------------------------------

        internal void Append(DebugPrimitive p)
        {
            int slot = Interlocked.Increment(ref _count) - 1;
            if ((uint)slot < (uint)_primitives.Length)
                _primitives[slot] = p;
            else
                Interlocked.Increment(ref _droppedCount);

            // Persist primitives with a positive lifetime.
            if (p.LifetimeSeconds > 0f)
            {
                int pSlot = Interlocked.Increment(ref _persistentCount) - 1;
                if ((uint)pSlot < (uint)_persistent.Length)
                {
                    _persistent[pSlot]    = p;
                    _remainingLife[pSlot] = p.LifetimeSeconds;
                }
                else
                {
                    Interlocked.Decrement(ref _persistentCount);
                    Interlocked.Increment(ref _droppedCount);
                }
            }
        }
    }
}

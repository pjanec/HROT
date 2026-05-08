extern alias GizmoMapContracts;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Fdp.Core;
// Alias for the GizmoMap-side FixedString32 used in DebugPrimitive fields.
using GizmoStr = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

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

        // Persistent re-emission: primitives with LifetimeSeconds > 0 survive across frames.
        private readonly DebugPrimitive[] _persistent;
        private readonly float[] _remainingLife;
        private int _persistentCount;
        private const int PersistentCapacity = 256;

        // Number of primitives dropped due to capacity overflow.
        public int DroppedCount => _droppedCount;

        // The intern map used by DrawTextLong. Exposed for consumers that resolve long-text hashes.
        public StringInternMap InternMap => _internMap;

        public DebugPrimitiveBuffer(int capacity = 4096, StringInternMap? internMap = null)
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
        /// Used by network ingress (<see cref="DebugPrimitivesIngressTranslator"/>) to restore
        /// received primitives. Thread-safe (uses Interlocked).
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
        /// systems execute (owned by DataDrivenGizmoSystem).
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
            // Fdp.Core.FixedString32 and GizmoMap.Contracts.FixedString32 share identical
            // 32-byte sequential layout; reinterpret for the MakeText factory method.
            var gizmoText = Unsafe.As<FixedString32, GizmoStr>(ref text);
            Append(DebugPrimitive.MakeText(x, y, gizmoText, color, space, layer));
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
            // Construct via Fdp.Core.FixedString32 (same layout) then reinterpret.
            var coreStr = new FixedString32(text);
            p.TextContent = Unsafe.As<FixedString32, GizmoStr>(ref coreStr);
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
            // Fdp.Core.FixedString32 and GizmoMap.Contracts.FixedString32 have identical layout.
            p.BadgeRichText    = Unsafe.As<FixedString32, GizmoStr>(ref richText);
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

        public void DrawEntityLocalInteractive(
            Entity anchor, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, ushort subElementId,
            float thickness = 1f, byte layer = 0)
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
            p.SubElementId     = subElementId;
            Append(p);
        }

        // ---- Internal helpers -----------------------------------------------

        // GZ057: SpatialAnchor and SemanticShape emit implementations.

        public void DrawSpatialAnchor(
            long  networkId,
            float worldX,
            float worldY,
            float worldZ,
            float headingDeg,
            byte  layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.SpatialAnchor;
            p.TargetView   = PipelineTarget.All;
            p.DebugLayer   = layer;
            p.NetworkId    = networkId;
            p.AnchorWorldX = worldX;
            p.AnchorWorldY = worldY;
            p.AnchorWorldZ = worldZ;
            p.Heading      = headingDeg;
            Append(p);
        }

        public void DrawSemanticShape(
            long   networkId,
            ulong  profileId,
            float  lengthMeters  = 0f,
            float  widthMeters   = 0f,
            uint   conditionMask = 0,
            byte   layer         = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape         = DebugPrimitiveShape.SemanticShape;
            p.Space         = CoordinateSpace.EntityLocal;
            p.TargetView    = PipelineTarget.All;
            p.DebugLayer    = layer;
            p.AnchorIndex   = (int)networkId;
            p.ProfileId     = profileId;
            p.LengthMeters  = lengthMeters;
            p.WidthMeters   = widthMeters;
            p.ConditionMask = conditionMask;
            Append(p);
        }

        public void DrawContextMenuBinding(long networkId, string menuJson)
        {
            uint hash = StringInternMap.Fnv1a32(menuJson);
            _internMap.Intern(hash, menuJson);   // idempotent; allocates only on first call
            Append(DebugPrimitive.MakeContextMenuBinding(networkId, hash));
        }

        internal void Append(DebugPrimitive p)
        {            int slot = Interlocked.Increment(ref _count) - 1;
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

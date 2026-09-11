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

        // Snapshot of the current transient write cursor (clamped to capacity).
        // Safe to call from the same thread that calls UpdateAndDraw. Used as a
        // mark before a gizmo draws so that StampGizmoTypeId can identify which
        // primitives the gizmo emitted.
        public int Count => Math.Min(_count, _primitives.Length);

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

        // IDebugDrawBuilder.EmitRaw -- forwards to AppendRaw so the interaction manager
        // can inject InputCaptureBinding meta-primitives via the draw builder interface.
        public void EmitRaw(in DebugPrimitive prim) => AppendRaw(in prim);

        /// <summary>
        /// Stamps <paramref name="gizmoTypeId"/> into the <see cref="DebugPrimitive.GizmoTypeId"/>
        /// field of every transient primitive in the half-open range [<paramref name="fromIndex"/>,
        /// <see cref="Count"/>). Only shapes for which offset 60 is free are written:
        /// <see cref="DebugPrimitiveShape.Box2D"/>, <see cref="DebugPrimitiveShape.StructInspector"/>,
        /// and <see cref="DebugPrimitiveShape.ContextMenuBinding"/>.
        /// SemanticShape, SpatialAnchor, and all other shapes are skipped to avoid corrupting their
        /// payload (SemanticShape.ResolvedRollRad shares offset 60).
        /// Persistent primitives (<c>_persistent</c>) are NOT stamped.
        /// </summary>
        public void StampGizmoTypeId(int fromIndex, uint gizmoTypeId)
        {
            int to = Count;
            for (int i = fromIndex; i < to; i++)
            {
                ref var p = ref _primitives[i];
                if (p.Shape == DebugPrimitiveShape.Box2D ||
                    p.Shape == DebugPrimitiveShape.StructInspector ||
                    p.Shape == DebugPrimitiveShape.ContextMenuBinding)
                {
                    p.GizmoTypeId = gizmoTypeId;
                }
            }
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
            // StringHash is always 0 for inline FixedString32 mode.
            // Fdp.Core.FixedString32 and GizmoMap.Contracts.FixedString32 share identical
            // 32-byte sequential layout; reinterpret for the MakeText factory method.
            var gizmoText = Unsafe.As<FixedString32, GizmoStr>(ref text);
            Append(DebugPrimitive.MakeText(x, y, gizmoText, color, space, layer, fontSizePx, lineOffsetPx));
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
            // Store first MaxLength chars inline as a preview; FixedString32 auto-truncates.
            // Construct via Fdp.Core.FixedString32 (same layout) then reinterpret.
            var coreStr = new FixedString32(text);
            p.TextContent = Unsafe.As<FixedString32, GizmoStr>(ref coreStr);
            // ThicknessU16 repurposed for Text: carries desired screen-pixel font size (not * 10).
            if (fontSizePx > 0f)
                p.ThicknessU16 = (ushort)fontSizePx;
            // Offset 12 carries the screen-pixel line offset for Text primitives (S6, signed).
            if (lineOffsetPx != 0f)
                p.LineOffsetPx = (short)lineOffsetPx;
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
            long anchorNetworkId, Vector3 localStart, Vector3 localEnd,
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
            // ⭐⭐⭐ CE-259z — offset 8 is the SpatialAnchor cache KEY, a NETWORK id.
            //   ⛔ It used to be `anchor.Index` (an ECS index) against a cache keyed by
            //     SpatialAnchor.NetworkId ⇒ the lookup missed and the primitive was SILENTLY SKIPPED.
            //   ⛔ And no AnchorGeneration is stamped: the generation is not part of the key, and
            //     stamping it here is what made offset 12 look like an identity component.
            AssertFitsAnchorKey(anchorNetworkId);
            p.AnchorIndex      = (int)anchorNetworkId;
            p.LineStart        = localStart;
            p.LineEnd          = localEnd;
            Append(p);
        }

        public void DrawEntityLocalInteractive(
            long anchorNetworkId, Vector3 localStart, Vector3 localEnd,
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
            // ⭐ CE-259z — see DrawEntityLocal.
            // ⛔⛔ AND NO `BoxAnchorId`, THOUGH IT WOULD BE THE IDENTITY (S5) — IT PHYSICALLY DOES NOT
            //   FIT. Measured 2026-09-10: for a Line the payload union is LineStart @24-35 and LineEnd
            //   @36-47, with EndColor @48-51; `BoxAnchorId` is a long @44-51 ⇒ it OVERLAPS LineEnd.Z
            //   AND EndColor. Stamping it corrupts the geometry, or is silently overwritten by it
            //   (which is what the first attempt did — the rail read back 0).
            //   ⇒ ⭐⭐ THIS IS THE DEEPER REASON A LINE CANNOT BE INTERACTIVE, and it is stronger than
            //     "the hit-test does not handle Line" (CE-259ac): a Line has NO SLOT FOR AN IDENTITY.
            //     Anything pickable must be Box2D or Sphere, whose payloads leave offset 44 free.
            AssertFitsAnchorKey(anchorNetworkId);
            p.AnchorIndex      = (int)anchorNetworkId;
            p.LineStart        = localStart;
            p.LineEnd          = localEnd;
            p.SubElementId     = subElementId;
            Append(p);
        }

        public void DrawEntitySphere(
            long    anchorNetworkId,
            Vector3 worldCenter,
            float   radius,
            Rgba32  color,
            byte    layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Sphere;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = SizeMode.WorldMeters;
            p.TargetView       = PipelineTarget.Map2D;
            p.Color            = color;
            p.SphereCenter     = worldCenter;
            p.SphereRadius     = radius;
            p.DebugLayer       = layer;
            // ⭐⭐ §6.7 — IDENTITY, in the field the hit-test actually routes on. ⛔ This was
            //   `AnchorIndex = anchor.Index; AnchorGeneration = anchor.Generation` — an ECS handle in
            //   the two offsets nothing reads for identity any more, which left the sphere pickable in
            //   its doc comment and unpickable in fact. ⭐ A Sphere's payload (SphereCenter @24-35,
            //   SphereRadius @36-39) leaves offset 44 free, so BoxAnchorId fits — unlike a Line, whose
            //   LineEnd/EndColor overlap it (see DebugPrimitive.MakePickSegment's note).
            p.BoxAnchorId      = anchorNetworkId;
            Append(p);
        }

        /// <summary>
        /// ⭐⭐ <b>C7 / CE-259z — the <c>EntityLocal</c> anchor key is 32 bits and the narrowing is
        /// unchecked.</b> An id above <c>int.MaxValue</c> wraps, misses the <c>SpatialAnchor</c> cache and
        /// the primitive is skipped in silence. ⛔ It cannot be widened (<c>SemanticShape</c>'s payload
        /// union is full; 64 bytes is a DDS invariant) ⇒ assert, do not wrap.
        /// ⚠ <c>Debug.Assert</c>, not a throw: a diagnostic emitter must never take down a frame, and
        /// production ids count from 1 (<c>SequentialIdAllocator</c>), so this is a latent limit.
        /// </summary>
        private static void AssertFitsAnchorKey(long anchorNetworkId)
            => System.Diagnostics.Debug.Assert(
                   anchorNetworkId >= int.MinValue && anchorNetworkId <= int.MaxValue,
                   $"EntityLocal anchor id {anchorNetworkId} does not fit the 32-bit SpatialAnchor cache "
                 + "key; the primitive would silently fail to resolve. See DebugPrimitive.cs offset 8.");

        // ---- Internal helpers -----------------------------------------------

        // GZ057: SpatialAnchor and SemanticShape emit implementations.

        public void DrawSpatialAnchor(
            long  networkId,
            float worldX,
            float worldY,
            float worldZ,
            float headingDeg,
            float pitchDeg = 0f,
            float rollDeg = 0f,
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
            p.Pitch        = pitchDeg;
            p.Roll         = rollDeg;
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
            // ⭐⭐⭐ S7 (DESIGN_Gizmo_Anchor_Identity.md §6, CE-259z) — offset 8 is the SpatialAnchor
            //   cache KEY for an EntityLocal primitive, and it is 32 bits wide. The cache is FILLED with
            //   the full 64-bit SpatialAnchor.NetworkId (DebugPrimitiveRenderer2D:63) and PROBED with
            //   this value widened back to long (:105), so an id above int.MaxValue wraps here and the
            //   lookup misses -- the shape is silently skipped, never drawn.
            //   ⛔ It cannot be widened: SemanticShape's payload union is full and 64 bytes is a
            //     DDS-marshalled invariant. ⇒ assert instead of wrapping in silence.
            //   ⚠ Debug.Assert, not a throw: a diagnostic emitter must never take down a frame, and
            //     production ids count from 1 (SequentialIdAllocator) so this is a latent limit, not a
            //     live failure. A throw here would be a new way to lose the map.
            System.Diagnostics.Debug.Assert(
                networkId >= int.MinValue && networkId <= int.MaxValue,
                $"SemanticShape anchor id {networkId} does not fit the 32-bit EntityLocal anchor key; " +
                "the primitive would silently fail to resolve. See DebugPrimitive.cs offset 8.");
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

        public void DrawMainMenuBinding(string menuJson)
        {
            uint hash = StringInternMap.Fnv1a32(menuJson);
            _internMap.Intern(hash, menuJson);   // idempotent; allocates only on first call
            Append(DebugPrimitive.MakeMainMenuBinding(hash));
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

using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // 64-byte blittable tagged union. One cache line. All payloads share offsets 24-63.
    //
    // Offset 8 (AnchorIndex/StringHash): when Space == EntityLocal, int AnchorIndex encodes
    // the anchor's NETWORK id, narrowed to 32 bits (§6.7 / C7 — never an ECS index).
    // When Space != EntityLocal and Shape is Text or EntityBadge,
    // uint StringHash at the same offset encodes the string intern map key (StringHash != 0
    // means the full text is resolved from StringInternMap; StringHash == 0 = inline mode).
    //
    // Icon layout note: Icon uses float IconWorldPosX/Y at [24]/[28] (2D world position)
    // so that FixedString32 IconAtlasCoord fits at [32]-[63] within the 64-byte boundary.
    // A Vector3 at [24] would push the FixedString32 to offset 36, exceeding 64 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public unsafe struct DebugPrimitive
    {
        // ---- Header (offsets 0-23) -----------------------------------------

        [FieldOffset(0)]  public DebugPrimitiveShape Shape;
        [FieldOffset(1)]  public CoordinateSpace Space;
        [FieldOffset(2)]  public Rgba32 Color;         // 4 bytes, ends at offset 6
        [FieldOffset(6)]  public PipelineTarget TargetView;
        [FieldOffset(7)]  public byte DebugLayer;      // 0-15

        // Bytes 8-11 overlay: AnchorIndex for EntityLocal; StringHash for intern escaping.


        // ⭐⭐ OFFSET 8 CARRIES TWO THINGS, discriminated by Shape/Space. S7/§6.7,
        //   DESIGN_Gizmo_Anchor_Identity.md. ⛔ NEITHER is an identity for INTERACTION -- that is
        //   BoxAnchorId (offset 44); see its note.
        //
        //   🔴🔴 §6.7, 2026-09-11 — ARM (a) IS GONE, AND IT WAS THE THIRD ROLE:
        //       "(a) an interactive Box2D/Sphere handle -> the ECS entity INDEX, an in-process PAYLOAD
        //        that lets the consumer-side adapter rebuild Entity(index, generation) with no lookup."
        //     ⛔ A process-local ECS handle, in a DDS-marshalled struct, in the same 4 bytes that mean
        //       a network id for the shape next to it. Its whole purpose was to spare the consumer a
        //       lookup — and the reason given for that was ONE module with no NetworkEntityMap.
        //       🔒 User: "replaybrowser is ecs module like any else. i do not want such exceptions."
        //     ⇒ no producer writes an ECS index here now, and no consumer reads one. ⭐ The union is
        //       one role narrower, which is the real win: a three-role field cannot be reasoned about.
        //
        //     (b) SemanticShape / any EntityLocal primitive -> the SpatialAnchor cache KEY, which is a
        //         NETWORK id (DebugPrimitiveBuffer.DrawSemanticShape writes `(int)networkId`;
        //         DebugPrimitiveRenderer2D:104-106 reads `(long)AnchorIndex` against a cache keyed by
        //         SpatialAnchor.NetworkId at offset 24).
        //     (c) Text / EntityBadge with Space != EntityLocal -> StringHash, below.
        //
        //   ⛔⛔ HARD LIMIT, measured 2026-09-10 (CE-259z): arm (b) TRUNCATES a 64-bit network id to
        //     int. The cache is written with the full `long` SpatialAnchor.NetworkId and read with an
        //     int-widened AnchorIndex, so an EntityLocal primitive whose anchor id exceeds int.MaxValue
        //     SILENTLY FAILS TO RESOLVE and the shape is skipped (`continue`).
        //     ⭐ It cannot be widened here: SemanticShape's 40-byte payload union is full (ProfileId at
        //       24-31, Length/Width at 32-39, ConditionMask at 40-43, Resolved* at 44-63) and the
        //       64-byte size is a DDS-marshalled invariant. ⇒ it is a CONSTRAINT, not a slot to find.
        //     ⭐⭐ So: an id used as an EntityLocal ANCHOR must stay <= int.MaxValue. Production ids do
        //       (SequentialIdAllocator counts from 1), and the disjoint TOOL range (1L<<40) is above it
        //       BY DESIGN -- safe only because no tool emits an EntityLocal primitive. DrawSemanticShape
        //       now asserts this rather than wrapping in silence.
        [FieldOffset(8)]  public int AnchorIndex;

        [FieldOffset(8)]  public uint StringHash;

        // ⭐⭐⭐ OFFSET 12 NOW CARRIES ONE THING: LineOffsetPx, for Text and EntityBadge. S6/§6.7,
        //   DESIGN_Gizmo_Anchor_Identity.md.
        //
        //   🔴🔴 §6.7, 2026-09-11 — `AnchorGeneration` HAS NO PRODUCER AND NO CONSUMER LEFT.
        //     It held "the ECS generation" for interactive and EntityLocal shapes, the other half of the
        //     pick payload deleted from offset 8. Every writer is gone (MakeBox2D's ECS overload,
        //     MakePickSegment, MakeSemanticShape, DrawEntitySphere, and the three tool gizmos that
        //     stamped it by hand), and so is every reader (MakePickToken, PickTopmostEntityAnchor,
        //     EcsDebugPrimitiveExtensions.GetAnchor).
        //   ⚠ THE FIELD ITSELF IS KEPT, deliberately: it is the ushort ALIAS of LineOffsetPx below, and
        //     the S6 pairing is what stops anyone writing `unchecked((ushort)(short)x)` again. ⛔ So do
        //     not read a value here as a generation — for a Text primitive it is a signed pixel offset.
        //   ⛔ It is not an IDENTITY either. Identity is BoxAnchorId (offset 44) for a hit-testable
        //     shape and StructNetworkId (offset 24) for a binding, and it is a NETWORK id.
        [FieldOffset(12)] public ushort AnchorGeneration; // ⚠ §6.7: no longer an ECS generation — the unsigned alias of LineOffsetPx.

        // ⭐ The SAME two bytes, read as signed. Negative moves a text line UP, positive DOWN.
        //   ⭐⭐ This alias exists so nobody writes `unchecked((ushort)(short)x)` on the way in and
        //     `(short)x` on the way out again -- the compiler does it, and the two casts were the only
        //     thing making a signed value look like a generation. Producers: DebugPrimitive.MakeText,
        //     DebugPrimitiveBuffer.DrawText (both copies). Consumers: DebugPrimitiveRenderer2D:345,:360.
        [FieldOffset(12)] public short LineOffsetPx;
        [FieldOffset(14)] public SizeMode SizeMode;
        [FieldOffset(15)] public byte ZIndex;           // intra-layer sort; 0=background
        [FieldOffset(16)] public ushort ThicknessU16;   // thickness * 10 (max 6553.5)
        [FieldOffset(18)] public byte MinZoomLod;        // 0=no limit; n*0.25=min zoom
        [FieldOffset(19)] public byte MaxZoomLod;        // 0=no limit; n*0.25=max zoom
        [FieldOffset(20)] public float LifetimeSeconds;  // 0=one frame; >0=persists

        // ---- Payload union (offsets 24-63, 40 bytes) -----------------------

        // Line payload
        [FieldOffset(24)] public Vector3 LineStart;     // 12 bytes (24-35)
        [FieldOffset(36)] public Vector3 LineEnd;       // 12 bytes (36-47)
        [FieldOffset(48)] public Rgba32 EndColor;       // 4 bytes (48-51) — gradient end

        // Sphere payload (overlaps Line at 24)
        [FieldOffset(24)] public Vector3 SphereCenter;
        [FieldOffset(36)] public float SphereRadius;

        // Box2D payload
        [FieldOffset(24)] public float BoxCenterX;
        [FieldOffset(28)] public float BoxCenterY;
        [FieldOffset(32)] public float BoxExtentX;
        [FieldOffset(36)] public float BoxExtentY;
        [FieldOffset(40)] public float BoxAngleDeg;
        // ⭐⭐⭐ Offset 44: BoxAnchorId -- THE IDENTITY, AND IT IS ALWAYS A NETWORK ID.
        //   Every hit-testable primitive stamps it: an entity pick box / handle carries the entity's
        //   NetworkIdentity value, a stateless tool handle carries the tool's own id from a DISJOINT
        //   high range (GlobalGizmoManager.ToolAnchorIdBase = 1L<<40), and -1L is the canvas sentinel.
        //   ⇒ the terminal compares ONLY this field, and puts ONLY this value in GizmoPickToken.AnchorId.
        //
        //   ⛔⛔ SUPERSEDED 2026-09-10 (S5, DESIGN_Gizmo_Anchor_Identity.md). This comment used to read:
        //     "When AnchorGeneration == 0, ... this field carries the authoritative 64-bit ID ...
        //      When AnchorGeneration != 0, this field is ignored and the terminal routes the ECS
        //      AnchorIndex instead."
        //   🔴 That rule was the CAUSE of two defects, not a description of a design: tool ids (1,2,3...)
        //     and ECS indices share the small-integer range, so an exclusive tool's capture admitted
        //     whichever entity's index equalled its id (D1); and the ECS index -- process-local -- went
        //     on the DDS wire, mis-targeting on the receiver (D2). AnchorIndex/AnchorGeneration are now
        //     an IN-PROCESS PAYLOAD only: never compared, never routed, never marshalled as an identity.
        //
        //   Overlaps ArrowHeadSize/EndColor (different shape -- no conflict).
        [FieldOffset(44)] public long BoxAnchorId;

        // Arrow payload
        [FieldOffset(24)] public Vector3 ArrowFrom;
        [FieldOffset(36)] public Vector3 ArrowTo;
        [FieldOffset(48)] public float ArrowHeadSize;
        // SubElementId: used by interactive EntityLocal primitives to distinguish handles.
        [FieldOffset(52)] public ushort SubElementId;
        [FieldOffset(54)] public LineStyle LineStyle;
        [FieldOffset(56)] public Rgba32 FillColor;

        // Text payload: 2D position at 24/28, content at 32 (ends at 64 exactly)
        [FieldOffset(24)] public float TextX;
        [FieldOffset(28)] public float TextY;
        [FieldOffset(32)] public FixedString32 TextContent; // 32 bytes (32-63)

        // EntityBadge payload: target entity at 24-29, rich text aliased at 32
        [FieldOffset(24)] public int BadgeTargetIndex;
        [FieldOffset(28)] public ushort BadgeTargetGen;
        // bytes 30-31 are unused padding

        // Icon payload: 2D position at 24/28, atlas coord aliased at 32
        [FieldOffset(24)] public float IconWorldPosX;
        [FieldOffset(28)] public float IconWorldPosY;
        // IconAtlasCoord aliases TextContent (same physical offset 32)

        // StructInspector payload (generic struct editor projected via StructEdit schema)
        [FieldOffset(24)] public long StructNetworkId;    // stable network-level anchor ID
        // InspNetworkId aliases StructNetworkId at the same offset; used by InputCaptureBinding
        // and ContextMenuBinding meta-primitives to carry the owning tool's anchor ID.
        [FieldOffset(24)] public long InspNetworkId;
        [FieldOffset(32)] public uint StructSchemaHash;   // FNV-1a hash of the StructEdit schema
        [FieldOffset(36)] public ScreenAnchor StructAnchor;
        [FieldOffset(37)] public byte StructIsReadOnly;
        // bytes 38-39 unused padding
        [FieldOffset(40)] public float StructOffsetX;
        [FieldOffset(44)] public float StructOffsetY;
        // bytes 48-63 unused

        // LayerControlMask payload: 32-byte 256-bit visibility mask at offsets 24-55
        [FieldOffset(24)] public LayerMask256 ActiveLayers;

        // SemanticShape payload: entity semantic shape/profile primitive.
        [FieldOffset(24)] public ulong ProfileId;       // DIS enumeration / shape profile registry key
        [FieldOffset(32)] public float LengthMeters;    // overall platform length (0 = use profile default)
        [FieldOffset(36)] public float WidthMeters;     // overall platform width (0 = use profile default)
        [FieldOffset(40)] public uint  ConditionMask;   // EntityShapeCondition bitfield (e.g. Damaged, Firing)


        /// <summary>
        /// Absolute world coordinates calculated locally by the client's two-pass renderer.
        /// OVER THE NETWORK: These fields act as unused padding and transmit as zeros.
        /// </summary>
        /// <remarks>
        /// The DebugPrimitive has an inviolable 64-byte limit to fit exactly in one CPU cache line. 
        /// A full 3D transformplus SemanticShape data overflows the 40-byte payload union budget.
        /// 
        /// To solve this, the host transmits a SpatialAnchor primitive and a SemanticShape primitive 
        /// separately over the network. When the dumb terminal receives them, it uses a two-pass renderer:
        /// 1. Pass 1 (Cache): Finds and caches all SpatialAnchors by their NetworkId.
        /// 2. Pass 2 (Resolve): Finds SemanticShapes, looks up their anchor, calculates the absolute 
        ///    world coordinates, and mutates this primitive in-place by overwriting its unused 
        ///    memory padding with these Resolved fields.
        /// 
        /// This allows the final rendering pass to draw the shape with zero additional dictionary lookups.
        /// </remarks>
        [FieldOffset(44)] public float ResolvedWorldX;
        [FieldOffset(48)] public float ResolvedWorldY;
        [FieldOffset(52)] public float ResolvedYawRad;
        [FieldOffset(56)] public float ResolvedPitchRad;
        [FieldOffset(60)] public float ResolvedRollRad;

        // GizmoTypeId: FNV-1a hash of the IGizmoDefinition implementing type's full name.
        // Used as a composite routing key (entity + GizmoTypeId) so multiple gizmos on the
        // same entity can be disambiguated.
        //
        // Offset 60 is free for Box2D (BoxAnchorId long ends at offset 52), Arrow (ArrowHeadSize
        // at 48-51, SubElementId at 52-53, LineStyle at 54-55, FillColor at 56-59), StructInspector
        // (StructOffsetY at 44-47, bytes 48-63 unused), and ContextMenuBinding (sparse payload).
        //
        // NOTE: SemanticShape.ResolvedRollRad also occupies offset 60. Corruption is prevented by
        // shape-gated stamping (TASK-GZ065): StampGizmoTypeId only writes to Box2D, StructInspector,
        // and ContextMenuBinding -- never to SemanticShape or SpatialAnchor.
        [FieldOffset(60)] public uint GizmoTypeId;

        // MilStd2525 payload: NATO symbol at a world position.
        // SidcCode aliases TextContent at offset 32 (same physical storage).
        [FieldOffset(24)] public float MilWorldPosX;
        [FieldOffset(28)] public float MilWorldPosY;
        [FieldOffset(32)] public FixedString32 SidcCode; // e.g. "SFGPUCI--------" (15 chars + null); aliases TextContent

        // SpatialAnchor payload: pre-resolved world position and full 3D orientation.
        // Severs the renderer's dependency on SimTransform for decoupled map viewers.
        // Negative NetworkId values denote synthetic/ephemeral anchors (no backing ECS entity).
        [FieldOffset(24)] public long  NetworkId;       // globally stable network-level entity ID
        [FieldOffset(32)] public float AnchorWorldX;    // world X (East)
        [FieldOffset(36)] public float AnchorWorldY;    // world Y (North)
        [FieldOffset(40)] public float AnchorWorldZ;    // world Z (Up)
        [FieldOffset(44)] public float Heading;         // heading in degrees
        [FieldOffset(48)] public float Pitch;           // pitch in degrees
        [FieldOffset(52)] public float Roll;            // roll in degrees
        // bytes 56-63 unused

        // ---- Helper properties -----------------------------------------------

        // Thickness in logical units. ThicknessU16 stores value * 10.
        public float Thickness => ThicknessU16 * 0.1f;

        // BadgeRichText aliases TextContent (same physical offset 32).
        public FixedString32 BadgeRichText
        {
            get => TextContent;
            set => TextContent = value;
        }

        // IconAtlasCoord aliases TextContent (same physical offset 32).
        public FixedString32 IconAtlasCoord
        {
            get => TextContent;
            set => TextContent = value;
        }

        // ---- Static factory helpers ------------------------------------------

        public static DebugPrimitive MakeLine(
            Vector3 from, Vector3 to, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid)
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.Line;
            p.Color        = color;
            p.EndColor     = color;      // solid line: end == start color
            p.LineStyle    = style;
            p.TargetView   = target;
            p.DebugLayer   = layer;
            p.SizeMode     = sizeMode;
            p.ThicknessU16 = (ushort)(thickness * 10f);
            p.LineStart    = from;
            p.LineEnd      = to;
            return p;
        }

        public static DebugPrimitive MakeSphere(
            Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid)
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.Sphere;
            p.Color        = color;
            p.FillColor    = fillColor;
            p.LineStyle    = style;
            p.TargetView   = target;
            p.DebugLayer   = layer;
            p.SizeMode     = sizeMode;
            p.ThicknessU16 = (ushort)(thickness * 10f);
            p.SphereCenter = center;
            p.SphereRadius = radius;
            return p;
        }

        public static DebugPrimitive MakeBox2D(
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
            var p = default(DebugPrimitive);
            p.Shape = DebugPrimitiveShape.Box2D;
            p.Space = CoordinateSpace.World;
            p.Color = color;
            p.FillColor = fillColor;
            p.LineStyle = style;
            p.TargetView = target;
            p.DebugLayer = layer;
            p.SizeMode = sizeMode;
            p.ThicknessU16 = (ushort)(thickness * 10f);
            p.BoxCenterX = center.X;
            p.BoxCenterY = center.Y;
            p.BoxExtentX = extents.X;
            p.BoxExtentY = extents.Y;
            p.BoxAngleDeg = angleDeg;
            p.BoxAnchorId = anchorId;
            p.SubElementId = subElementId;
            return p;
        }

        // 🔴🔴 DELETED 2026-09-11 (§6.7) — the "ECS-anchored overload":
        //     MakeBox2D(center, extents, color, int anchorIndex, ushort anchorGeneration,
        //               long networkId, ...)
        //   ⛔ Its only job was to stamp the emitting process's ECS handle into offsets 8/12 on top of
        //     the base overload's networkId, so a picked primitive could hand a consumer a ready-made
        //     Entity. Nothing reads that any more: identity is BoxAnchorId and the consumer resolves it
        //     in its own world. 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6.7, GizmoPickToken.cs.
        //   ⭐ Callers moved to the base overload, which already takes `networkId` as its `anchorId` —
        //     i.e. the ECS-free path was always there; this overload only added the payload.

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-259ac</c> — A CLICKABLE LINE SEGMENT.</b> Returns a <see cref="DebugPrimitiveShape.Box2D"/>
        /// oriented along <paramref name="from"/>→<paramref name="to"/>, <paramref name="pickThickness"/>
        /// wide, so the terminal's hit-test picks the SEGMENT and not its bounding square.
        ///
        /// <para>⛔⛔ <b>Why a box and not a pickable <c>Line</c>.</b> A <c>Line</c> physically cannot carry
        /// an identity: its payload is <c>LineStart</c> @24-35 + <c>LineEnd</c> @36-47 with <c>EndColor</c>
        /// @48-51, while <see cref="BoxAnchorId"/> — the field the hit-test routes on — is a <c>long</c>
        /// @44-51 and overlaps both. Narrowing <c>LineEnd</c> to 2D would free exactly those 8 bytes, but
        /// <c>Stride/Hrot.Stride.Core/DebugPrimitiveRenderer3D.cs:222-223</c> draws lines from the full
        /// <c>Vector3</c>, so that is not available. ⇒ ⭐ a <c>Box2D</c> already has every field needed —
        /// centre, extents, <b>angle</b>, a full 64-bit <c>BoxAnchorId</c>, and <c>SubElementId</c> @52 for
        /// "which segment" — so no new shape and no layout change are required.</para>
        ///
        /// <para>⭐⭐ <b>This is the established pattern, generalised from a point to a segment.</b>
        /// <c>EntityPresentationGizmoShared.EmitPickBox</c> already gives an entity a fully transparent
        /// 8×8 <c>Box2D</c> purely as a pick target, separate from its visual. Emit your pretty
        /// <c>Line</c> (dashed, gradient, whatever) for looks and one of these for picking — or pass a
        /// visible colour and let this BE the visual, since the renderer draws rotated boxes correctly.</para>
        ///
        /// <para>⚠ Requires the oriented-box hit-test (<c>DebugGizmoLayer.FindTopmostInteractivePrimitive</c>).
        /// Before <c>2026-09-11</c> that test was axis-aligned, which is why lines were unpickable and why a
        /// rotated box drew rotated but picked square.</para>
        /// </summary>
        /// <param name="pickThickness">Full width of the pick corridor in world units (not a half-extent).
        /// The terminal adds its own ~5px grace radius on top, so a value of 0 still picks a thin line.</param>
        public static DebugPrimitive MakePickSegment(
            Vector2 from, Vector2 to,
            long networkId,
            float pickThickness = 0f,
            ushort subElementId = 0,
            Rgba32 color = default,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.Map2D,
            byte layer = 0)
        {
            var d = to - from;
            float length = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            // ⭐ A degenerate segment is a point: keep it pickable rather than emitting a zero-area box
            //   that only the grace radius could ever hit at exactly one spot.
            float angleDeg = length > 0f ? MathF.Atan2(d.Y, d.X) * (180f / MathF.PI) : 0f;

            var p = MakeBox2D(
                center: (from + to) * 0.5f,
                extents: new Vector2(length * 0.5f, pickThickness * 0.5f),
                color: color,
                angleDeg: angleDeg,
                thickness: 1f,
                sizeMode: sizeMode,
                target: target,
                layer: layer,
                anchorId: networkId,
                subElementId: subElementId);
            // 🔴 §6.7 — the `anchorIndex`/`anchorGeneration` parameters and their two writes are GONE.
            //   The segment's identity is `networkId` in BoxAnchorId; nothing reads an ECS handle here.
            return p;
        }

        public static DebugPrimitive MakeArrow(
            Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape         = DebugPrimitiveShape.Arrow;
            p.Color         = color;
            p.TargetView    = PipelineTarget.All;
            p.DebugLayer    = layer;
            p.ArrowFrom     = from;
            p.ArrowTo       = to;
            p.ArrowHeadSize = headSize;
            return p;
        }

        public static DebugPrimitive MakeText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0,
            float fontSizePx = 0f,
            float lineOffsetPx = 0f)
        {
            var p = default(DebugPrimitive);
            p.Shape       = DebugPrimitiveShape.Text;
            p.Space       = space;
            p.Color       = color;
            p.TargetView  = PipelineTarget.All;
            p.DebugLayer  = layer;
            p.TextX       = x;
            p.TextY       = y;
            p.TextContent = text;
            // StringHash remains 0 (inline mode)
            // ThicknessU16 is repurposed for Text to carry the desired screen-pixel font size
            // (stored as-is, not * 10 like line/sphere thickness). Zero means "use renderer default".
            if (fontSizePx > 0f)
                p.ThicknessU16 = (ushort)fontSizePx;
            // Offset 12 carries the screen-pixel line offset for Text primitives (S6).
            // Signed: negative moves the line UP, positive DOWN.
            if (lineOffsetPx != 0f)
                p.LineOffsetPx = (short)lineOffsetPx;
            return p;
        }

        // ContextMenuBinding payload reuses existing overlapping fields:
        //   StringHash      (offset 8)  - FNV-1a hash of the JSON menu string (same overlay as AnchorIndex)
        //   StructNetworkId (offset 24) - stable entity ID to bind the menu to
        // All other fields remain zero. This primitive is non-visual and never dispatched to the renderer.
        public static DebugPrimitive MakeContextMenuBinding(long networkId, uint menuJsonHash)
        {
            var p = default(DebugPrimitive);
            p.Shape           = DebugPrimitiveShape.ContextMenuBinding;
            p.StringHash      = menuJsonHash;   // FNV-1a hash of the JSON menu string
            p.StructNetworkId = networkId;      // entity to bind the menu to
            return p;
        }

        // InputCaptureBinding payload reuses existing overlapping fields:
        //   StructNetworkId (offset 24) - stable AnchorId of the capturing tool
        //   SubElementId    (offset 52) - handle id within the tool (0 = whole tool)
        //   ConditionMask   (offset 40) - bit 0: exclusive hit-testing, bit 1: raw input routing
        public static DebugPrimitive MakeInputCaptureBinding(
            long networkId, ushort subElementId, bool exclusive, bool wantsRawInput = false)
        {
            var p = default(DebugPrimitive);
            p.Shape           = DebugPrimitiveShape.InputCaptureBinding;
            p.StructNetworkId = networkId;
            p.SubElementId    = subElementId;
            p.ConditionMask   = (exclusive ? 1u : 0u) | (wantsRawInput ? 2u : 0u);
            return p;
        }

        // MainMenuBinding payload reuses StringHash (offset 8) for the interned JSON menu array hash.
        // All other fields remain zero. Non-visual meta-primitive consumed by MainMenuAdapter.
        /// <summary>
        /// ⭐⭐⭐ <b>THE IDENTITY INVARIANT — an INTERACTIVE primitive MUST carry one.</b>
        /// 🔒 User ruling, <c>2026-09-11</c>: *"how comes there could be gizmo with no identity? this
        /// should be hard-guarded. Identity was always a requirement so we can not drop it."*
        /// 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.8.
        ///
        /// <para>⛔⛔ <b>Why this never fired before, which is the whole answer to "how comes":</b>
        /// identity was a requirement stated in COMMENTS and enforced NOWHERE. Two checks existed and
        /// both test PRESENCE, not a usable value — <c>EntityPresentationGizmo</c>'s
        /// <c>[GizmoProjector(SimTransform, NetworkIdentity)]</c> query and
        /// <c>EmitPickSegments</c>'s <c>HasComponent&lt;NetworkIdentity&gt;</c>. ⇒ a component present with
        /// <c>Value == 0</c> passed both. ⭐⭐ And the ECS payload MASKED the violation: a primitive with
        /// no anchor id was still pickable, because the terminal forwarded the emitter's ECS handle and
        /// the consumer rebuilt an <c>Entity</c> from it. ⇒ breaking the rule had no symptom. Deleting
        /// the payload (§6.7) removed the mask, which is why this surfaced now and not earlier.</para>
        ///
        /// <para>⭐⭐ <b>Where the identity lives is shape-discriminated</b>, and the terminal's own
        /// hit-test is the authority:
        /// <list type="bullet">
        ///   <item>interactive + <c>EntityLocal</c> ⇒ offset 8 (<c>AnchorIndex</c>), the 32-bit
        ///   <c>SpatialAnchor</c> cache key — a <c>Line</c> has no room for <c>BoxAnchorId</c>;</item>
        ///   <item>interactive + any other space ⇒ <c>BoxAnchorId</c> (offset 44);</item>
        ///   <item>a BINDING ⇒ <c>StructNetworkId</c> (offset 24). ⚠ <c>-1</c> is LEGAL there — it is the
        ///   canvas sentinel the context menu uses — so the test is <c>!= 0</c>, never <c>&gt; 0</c>.</item>
        /// </list></para>
        ///
        /// <para>⭐⭐⭐ <b>IT LIVES HERE, ON THE PRIMITIVE, BECAUSE THERE ARE TWO BUFFERS.</b> 📐 Found by
        /// <c>search_graph</c> after a filename-based grep misled me: <c>GizmoMap.Contracts</c> holds
        /// <c>GizmoPrimitiveBuffer</c> — the ECS-FREE twin, with its own <c>Append</c>/<c>AppendRaw</c>,
        /// used by the standalone GizmoMap apps and by <c>Stride/HrotStrideApp.Game</c>. ⛔ Putting the
        /// invariant in <c>Fdp.Diagnostics.Contracts.DebugPrimitiveBuffer</c> alone would have enforced it
        /// on ONE of the two funnel pairs — the seam law, committed by the very change meant to remove a
        /// second identity channel. ⭐ The check reads only <c>DebugPrimitive</c> fields, so this assembly
        /// is its natural home and both buffers call it.</para>
        ///
        /// <para>⚠ <b><c>Debug.Assert</c>, not a throw</b>, and deliberately — the same ruling as
        /// <c>DebugPrimitiveBuffer.AssertFitsAnchorKey</c>: a diagnostics emitter must never take down a frame, and this
        /// runs on every primitive of every frame. ⭐ It fires in dev and in CI, where it can be acted on,
        /// and the invariant additionally has RAILS so it is enforced rather than merely observed. ⛔ The
        /// primitive is still appended: dropping it silently would trade a loud dev failure for an
        /// invisible production one.</para>
        /// </summary>
        public static void AssertHasIdentity(in DebugPrimitive p)
        {
            // ⭐⭐⭐ A SHAPE ALLOW-LIST, not a "looks interactive" heuristic — and the first cut of this
            //   check got that wrong, loudly, which is why it is spelled out.
            //   ⛔⛔ The first version tested `AnchorIndex != 0 || SubElementId != 0 || BoxAnchorId != 0`,
            //     mirroring the terminal's own pre-filter. 📐 Measured: that FALSE-POSITIVES on every
            //     shape whose offset 8 is a StringHash rather than an anchor key — interned `Text`,
            //     `EntityBadge`, and `MainMenuBinding` (which has NO identity by design: it is global).
            //     ⇒ 26 rails across 5 suites asserted, none of them a real violation.
            //   ⭐ The honest rule is the one the ROUTING uses: only a shape the hit-test can PICK needs
            //     a pick identity, and only an EntityLocal primitive needs an anchor KEY.
            switch (p.Shape)
            {
                // ── bindings: keyed by StructNetworkId (offset 24) ─────────────────────────────
                case DebugPrimitiveShape.InputCaptureBinding:
                case DebugPrimitiveShape.ContextMenuBinding:
                case DebugPrimitiveShape.StructInspector:
                    System.Diagnostics.Debug.Assert(
                        p.StructNetworkId != 0,
                        $"A {p.Shape} binding carries NO IDENTITY (StructNetworkId == 0). Nothing can "
                      + "ever route to it: the terminal keys bindings by this field. Pass the anchor's "
                      + "network id, a disjoint tool id (GlobalGizmoManager.ToolAnchorIdBase), or -1 for "
                      + "the canvas. See DESIGN_Gizmo_Anchor_Identity.md §6.8.");
                    return;

                // ⛔ MainMenuBinding is deliberately NOT here: it is a GLOBAL contribution with no
                //   anchor, and offset 8 is its interned JSON hash. LayerControlMask likewise.
                case DebugPrimitiveShape.MainMenuBinding:
                case DebugPrimitiveShape.LayerControlMask:
                case DebugPrimitiveShape.SpatialAnchor:   // it IS an identity source, not a consumer
                    return;
            }

            // ── an EntityLocal primitive of ANY shape needs the 32-bit anchor KEY at offset 8 ──
            if (p.Space == CoordinateSpace.EntityLocal)
            {
                System.Diagnostics.Debug.Assert(
                    p.AnchorIndex != 0,
                    $"An EntityLocal {p.Shape} carries NO ANCHOR KEY (offset 8 == 0). It resolves against "
                  + "no SpatialAnchor, so it is drawn nowhere and picks nothing. "
                  + "See DESIGN_Gizmo_Anchor_Identity.md §6.8.");
                return;
            }

            // ── a HIT-TESTABLE shape needs a pick identity ────────────────────────────────────
            //   ⚠ Only Box2D and Sphere: those are the shapes FindTopmostInteractivePrimitive tests,
            //     and the only ones whose payload leaves offset 44 free for BoxAnchorId (a Line's
            //     LineEnd/EndColor overlap it — see MakePickSegment's note).
            bool hitTestable = p.Shape == DebugPrimitiveShape.Box2D
                            || p.Shape == DebugPrimitiveShape.Sphere;
            if (!hitTestable) return;

            // ⚠ A hit-testable shape may legitimately be DECORATIVE — a plain DrawBox2D outline with no
            //   anchor and no sub-element is not a pick target and needs no identity.
            bool claimsInteraction = p.SubElementId != 0 || p.BoxAnchorId != 0;
            if (!claimsInteraction) return;

            System.Diagnostics.Debug.Assert(
                p.BoxAnchorId != 0,
                $"An interactive {p.Shape} (SubElementId {p.SubElementId}) carries NO IDENTITY "
              + "(BoxAnchorId == 0). It is WORSE than inert: a non-zero SubElementId still passes the "
              + "terminal's interactivity pre-filter, so it WINS the hit-test and swallows the click, "
              + "then resolves to no entity and reaches no gizmo. Identity is the anchor's network id "
              + "and it was always required. See DESIGN_Gizmo_Anchor_Identity.md §6.8.");
        }

        public static DebugPrimitive MakeMainMenuBinding(uint menuJsonHash)
        {
            var p = default(DebugPrimitive);
            p.Shape      = DebugPrimitiveShape.MainMenuBinding;
            p.StringHash = menuJsonHash;
            return p;
        }

        /// <summary>
        /// ⭐⭐ §6.7 — <paramref name="anchorKey"/> is the <c>SpatialAnchor</c> CACHE KEY at offset 8: the
        /// anchor's network id narrowed to 32 bits (constraint <c>C7</c>), which is what
        /// <c>DebugPrimitiveRenderer2D:105</c> probes the cache with. ⛔ It is NOT an ECS index, and the
        /// companion <c>ushort anchorGeneration</c> parameter — which stamped the emitter's ECS
        /// generation into offset 12 for nothing to read — is DELETED.
        /// </summary>
        public static DebugPrimitive MakeSemanticShape(
            int anchorKey, long networkId, ulong profileId,
            float length, float width, uint conditionMask,
            PipelineTarget target = PipelineTarget.All, byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape = DebugPrimitiveShape.SemanticShape;
            p.Space = CoordinateSpace.EntityLocal;
            p.TargetView = target;
            p.DebugLayer = layer;
            p.AnchorIndex = anchorKey;
            p.BoxAnchorId = networkId;
            p.ProfileId = profileId;
            p.LengthMeters = length;
            p.WidthMeters = width;
            p.ConditionMask = conditionMask;
            return p;
        }

        public static DebugPrimitive MakeStructInspector(
            long networkId,
            uint schemaHash,
            ScreenAnchor anchor = ScreenAnchor.TopLeft,
            float offsetX = 0f,
            float offsetY = 0f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            bool isReadOnly = false,
            PipelineTarget target = PipelineTarget.All)
        {
            var p = default(DebugPrimitive);
            p.Shape = DebugPrimitiveShape.StructInspector;
            p.TargetView = target;
            p.StructNetworkId = networkId;
            p.StructSchemaHash = schemaHash;
            p.StructAnchor = anchor;
            p.StructOffsetX = offsetX;
            p.StructOffsetY = offsetY;
            p.SizeMode = sizeMode;
            p.StructIsReadOnly = (byte)(isReadOnly ? 1 : 0);
            return p;
        }

        public static DebugPrimitive MakeLayerControlMask(
            LayerMask256 activeLayers,
            PipelineTarget target = PipelineTarget.All)
        {
            var p = default(DebugPrimitive);
            p.Shape = DebugPrimitiveShape.LayerControlMask;
            p.TargetView = target;
            p.ActiveLayers = activeLayers;
            return p;
        }

        public static DebugPrimitive MakeSpatialAnchor(
            long networkId, float worldX, float worldY, float worldZ,
            float headingDeg, float pitchDeg = 0f, float rollDeg = 0f,
            PipelineTarget target = PipelineTarget.All, byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.SpatialAnchor;
            p.TargetView   = target;
            p.DebugLayer   = layer;
            p.NetworkId    = networkId;
            p.AnchorWorldX = worldX;
            p.AnchorWorldY = worldY;
            p.AnchorWorldZ = worldZ;
            p.Heading      = headingDeg;
            p.Pitch        = pitchDeg;
            p.Roll         = rollDeg;
            return p;
        }
    }
}

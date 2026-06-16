using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // 64-byte blittable tagged union. One cache line. All payloads share offsets 24-63.
    //
    // Offset 8 (AnchorIndex/StringHash): when Space == EntityLocal, int AnchorIndex encodes
    // the entity anchor index. When Space != EntityLocal and Shape is Text or EntityBadge,
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


        // ECS Entity Index for primitives anchored to an entity.
        // An index of 0 is a perfectly valid memory offset in a data-oriented ECS.
        // Never use AnchorIndex to evaluate handle validity; evaluate AnchorGeneration instead.
        [FieldOffset(8)]  public int AnchorIndex;

        [FieldOffset(8)]  public uint StringHash;

        [FieldOffset(12)] public ushort AnchorGeneration; // ECS Entity Generation. A generation of 0 guarantees the handle is null or uninitialized.
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
        // Offset 44: BoxAnchorId -- Multiplexed interaction handle.
        // When AnchorGeneration == 0, the primitive is a stateless tool or network object,
        // and this field carries the authoritative 64-bit ID for managed hit-routing.
        // When AnchorGeneration != 0, this field is ignored and the terminal routes
        // the ECS AnchorIndex instead.
        // Overlaps ArrowHeadSize/EndColor (different shape -- no conflict).
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

        // ECS-anchored overload for interactive tools and pick-box primitives.
        public static DebugPrimitive MakeBox2D(
            Vector2 center, Vector2 extents, Rgba32 color,
            int anchorIndex, ushort anchorGeneration, long networkId,
            ushort subElementId = 0, float angleDeg = 0f, float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels, PipelineTarget target = PipelineTarget.All,
            byte layer = 0, Rgba32 fillColor = default, LineStyle style = LineStyle.Solid)
        {
            var p = MakeBox2D(center, extents, color, angleDeg, thickness, sizeMode, target, layer, fillColor, style, networkId, subElementId);
            p.AnchorIndex = anchorIndex;
            p.AnchorGeneration = anchorGeneration;
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
            // AnchorGeneration carries the screen-pixel line offset for Text primitives.
            // Signed: negative moves the line UP, positive DOWN (stored as int16 bit-pattern).
            if (lineOffsetPx != 0f)
                p.AnchorGeneration = unchecked((ushort)(short)lineOffsetPx);
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
        public static DebugPrimitive MakeMainMenuBinding(uint menuJsonHash)
        {
            var p = default(DebugPrimitive);
            p.Shape      = DebugPrimitiveShape.MainMenuBinding;
            p.StringHash = menuJsonHash;
            return p;
        }

        public static DebugPrimitive MakeSemanticShape(
            int anchorIndex, ushort anchorGeneration, long networkId, ulong profileId,
            float length, float width, uint conditionMask,
            PipelineTarget target = PipelineTarget.All, byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape = DebugPrimitiveShape.SemanticShape;
            p.Space = CoordinateSpace.EntityLocal;
            p.TargetView = target;
            p.DebugLayer = layer;
            p.AnchorIndex = anchorIndex;
            p.AnchorGeneration = anchorGeneration;
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

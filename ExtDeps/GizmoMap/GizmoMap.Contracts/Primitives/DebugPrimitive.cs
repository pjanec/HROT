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
        [FieldOffset(8)]  public int AnchorIndex;
        [FieldOffset(8)]  public uint StringHash;

        [FieldOffset(12)] public ushort AnchorGeneration;
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

        // Arrow payload
        [FieldOffset(24)] public Vector3 ArrowFrom;
        [FieldOffset(36)] public Vector3 ArrowTo;
        [FieldOffset(48)] public float ArrowHeadSize;
        // SubElementId: used by interactive EntityLocal primitives to distinguish handles.
        [FieldOffset(52)] public ushort SubElementId;

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

        // ComponentInspector payload
        [FieldOffset(24)] public long InspNetworkId;    // stable network-level entity ID (not ECS slot)
        [FieldOffset(32)] public uint InspSchemaHash;   // FNV-1a hash of the component type name
        [FieldOffset(36)] public ScreenAnchor InspAnchor;
        [FieldOffset(37)] public byte InspIsReadOnly;
        // bytes 38-39 unused padding
        [FieldOffset(40)] public float InspOffsetX;
        [FieldOffset(44)] public float InspOffsetY;
        // bytes 48-63 unused

        // SemanticShape payload: entity semantic shape/profile primitive.
        [FieldOffset(24)] public ulong ProfileId;       // DIS enumeration / shape profile registry key
        [FieldOffset(32)] public float LengthMeters;    // overall platform length (0 = use profile default)
        [FieldOffset(36)] public float WidthMeters;     // overall platform width (0 = use profile default)
        [FieldOffset(40)] public uint  ConditionMask;   // EntityShapeCondition bitfield (e.g. Damaged, Firing)
        // bytes 44-63 unused

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
            byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.Line;
            p.Color        = color;
            p.EndColor     = color;      // solid line: end == start color
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
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.Sphere;
            p.Color        = color;
            p.TargetView   = target;
            p.DebugLayer   = layer;
            p.SphereCenter = center;
            p.SphereRadius = radius;
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
            byte layer = 0)
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
            return p;
        }

        // ContextMenuBinding payload reuses existing overlapping fields:
        //   StringHash    (offset 8)  - FNV-1a hash of the JSON menu string (same overlay as AnchorIndex)
        //   InspNetworkId (offset 24) - stable entity ID to bind the menu to
        // All other fields remain zero. This primitive is non-visual and never dispatched to the renderer.
        public static DebugPrimitive MakeContextMenuBinding(long networkId, uint menuJsonHash)
        {
            var p = default(DebugPrimitive);
            p.Shape         = DebugPrimitiveShape.ContextMenuBinding;
            p.StringHash    = menuJsonHash;   // FNV-1a hash of the JSON menu string
            p.InspNetworkId = networkId;      // entity to bind the menu to
            return p;
        }
    }
}

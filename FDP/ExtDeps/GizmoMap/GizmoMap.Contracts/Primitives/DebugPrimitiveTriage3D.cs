using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>One primitive after the 3-D triage: resolved into WORLD space (FDP axes: X east, Y north, Z up).</summary>
    public struct TriagedPrimitive3D
    {
        /// <summary>The primitive, with an <see cref="CoordinateSpace.EntityLocal"/> payload rewritten into world coordinates
        /// (and <see cref="DebugPrimitive.Space"/> set to <see cref="CoordinateSpace.World"/>).</summary>
        public DebugPrimitive Primitive;

        /// <summary>True when the primitive was anchored to an entity (<see cref="CoordinateSpace.EntityLocal"/>).</summary>
        public bool Anchored;

        /// <summary>The anchor's network id (0 when not anchored).</summary>
        public long AnchorId;

        /// <summary>The anchor's height. ⚠ The 2-D payloads (<c>Box2D</c>, <c>Text</c>, <c>Icon</c>, <c>EntityBadge</c>,
        /// <c>MilStd2525</c>, <c>FilledTriangle</c>) have no Z slot — an anchored one stands at this height; an un-anchored one
        /// has none (0), which the 3-D map drapes on the ground (docs/DESIGN_Map_3D_Mode.md M18).</summary>
        public float AnchorZ;

        /// <summary>The anchor's resolved transform (yaw / pitch / roll, radians) — for a <c>SemanticShape</c>.</summary>
        public float YawRad, PitchRad, RollRad;
    }

    /// <summary>A count per <see cref="DebugPrimitiveShape"/> — what a 3-D consumer SKIPPED, so a 2-D-only gizmo vanishing in
    /// 3-D is visible rather than silent (DESIGN_Stride_Node_Modes.md §8, Q5).</summary>
    public sealed class ShapeCounters
    {
        private readonly int[] _counts = new int[32];

        /// <summary>The count for <paramref name="shape"/>.</summary>
        public int this[DebugPrimitiveShape shape] => (int)shape < _counts.Length ? _counts[(int)shape] : 0;

        /// <summary>The sum over every shape.</summary>
        public int Total
        {
            get { int t = 0; foreach (int c in _counts) t += c; return t; }
        }

        /// <summary>Adds one for <paramref name="shape"/>.</summary>
        public void Add(DebugPrimitiveShape shape)
        {
            if ((int)shape < _counts.Length) _counts[(int)shape]++;
        }

        /// <summary>Back to zero (start of a frame).</summary>
        public void Reset() => Array.Clear(_counts, 0, _counts.Length);
    }

    /// <summary>
    /// ⭐⭐ CE-1033 S3 — THE 3-D gizmo triage, ONE implementation for every 3-D consumer (the Stride shell and the 3-D map):
    /// 📄 docs/DESIGN_Map_3D_Mode.md S3 · DESIGN_Godot_3D_Viewer.md D9 (<i>"extract the triage, shared"</i>).
    ///
    /// <para><b>Pass 1</b> caches every <see cref="DebugPrimitiveShape.SpatialAnchor"/> by network id and reads the frame's
    /// <see cref="DebugPrimitiveShape.LayerControlMask"/>. <b>Pass 2</b> drops the meta-primitives, applies the filters the
    /// consumer asked for (target view, layer mask, LOD) and resolves <see cref="CoordinateSpace.EntityLocal"/> payloads against
    /// their anchor IN 3-D — the offset is turned by the anchor's heading and lifted by its height (the 2-D renderer's
    /// <c>ApplyAnchor2D</c> plus Z). What a consumer DRAWS of the result is its own business; this class draws nothing and has
    /// no engine types (BCL only — it lives beside <see cref="DebugPrimitive"/> so every engine can take it).</para>
    ///
    /// <para>⚠ The default filters are OFF so the Stride shell keeps exactly what it drew before the extraction; the 3-D map
    /// turns them on (it is a map — the layer mask and the LOD the 2-D map honours apply to it too).</para>
    /// </summary>
    public sealed class DebugPrimitiveTriage3D
    {
        private const float DegToRad = MathF.PI / 180f;
        private readonly Dictionary<long, TriagedPrimitive3D> _anchors = new Dictionary<long, TriagedPrimitive3D>();

        /// <summary>The targets a primitive must name one of to be kept. Default <see cref="PipelineTarget.All"/> (no filter).
        /// ⚠ The map passes <c>Map2D | Viewport3D</c>: every gizmo today targets <c>Map2D</c> only (docs/DESIGN_Map_3D_Mode.md §7).</summary>
        public PipelineTarget AcceptedTargets { get; set; } = PipelineTarget.All;

        /// <summary>Honour the frame's <see cref="DebugPrimitiveShape.LayerControlMask"/> as the 2-D renderer does. Default off.</summary>
        public bool HonourLayerMask { get; set; }

        /// <summary>The LOD's zoom at a world point (pixels per metre there) — the 3-D equivalent of the 2-D map's one zoom.
        /// Null ⇒ no LOD culling.</summary>
        public Func<Vector3, float>? ZoomAt { get; set; }

        /// <summary>Primitives dropped in the last <see cref="Triage"/> by each filter, and those whose anchor was missing.</summary>
        public int CulledByTarget { get; private set; }
        /// <inheritdoc cref="CulledByTarget"/>
        public int CulledByLayer { get; private set; }
        /// <inheritdoc cref="CulledByTarget"/>
        public int CulledByLod { get; private set; }
        /// <inheritdoc cref="CulledByTarget"/>
        public int DanglingAnchors { get; private set; }

        /// <summary>Triages <paramref name="primitives"/> into <paramref name="into"/> (cleared first), in emission order.
        /// Returns the number kept.</summary>
        public int Triage(ReadOnlySpan<DebugPrimitive> primitives, List<TriagedPrimitive3D> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            CulledByTarget = CulledByLayer = CulledByLod = DanglingAnchors = 0;

            var layers = new LayerMask256();
            layers.SetAll();
            _anchors.Clear();
            foreach (ref readonly var p in primitives)
            {
                if (p.Shape == DebugPrimitiveShape.SpatialAnchor)
                {
                    _anchors[p.NetworkId] = new TriagedPrimitive3D
                    {
                        Anchored = true, AnchorId = p.NetworkId, AnchorZ = p.AnchorWorldZ,
                        YawRad = p.Heading * DegToRad, PitchRad = p.Pitch * DegToRad, RollRad = p.Roll * DegToRad,
                        Primitive = p,
                    };
                }
                else if (p.Shape == DebugPrimitiveShape.LayerControlMask)
                {
                    layers = p.ActiveLayers;
                }
            }

            foreach (ref readonly var p in primitives)
            {
                switch (p.Shape)
                {
                    case DebugPrimitiveShape.SpatialAnchor:
                    case DebugPrimitiveShape.ContextMenuBinding:
                    case DebugPrimitiveShape.InputCaptureBinding:
                    case DebugPrimitiveShape.MainMenuBinding:
                    case DebugPrimitiveShape.LayerControlMask:
                        continue;
                }

                if ((p.TargetView & AcceptedTargets) == 0) { CulledByTarget++; continue; }
                // ⭐ CE-3149 — a panel is not a map layer (the 2-D renderer's exemption, kept identical).
                if (HonourLayerMask && p.Shape != DebugPrimitiveShape.StructInspector && !layers.IsSet(p.DebugLayer))
                { CulledByLayer++; continue; }

                var t = new TriagedPrimitive3D { Primitive = p };
                if (p.Space == CoordinateSpace.EntityLocal)
                {
                    if (!_anchors.TryGetValue(p.AnchorIndex, out var a)) { DanglingAnchors++; continue; }
                    t.Anchored = true;
                    t.AnchorId = a.AnchorId;
                    t.AnchorZ = a.AnchorZ;
                    t.YawRad = a.YawRad; t.PitchRad = a.PitchRad; t.RollRad = a.RollRad;
                    Resolve(ref t.Primitive, in a.Primitive, a.YawRad);
                    t.Primitive.Space = CoordinateSpace.World;
                }

                if (ZoomAt != null && (p.MinZoomLod != 0 || p.MaxZoomLod != 0))
                {
                    float zoom = ZoomAt(PositionOf(in t));
                    if ((p.MinZoomLod != 0 && zoom < p.MinZoomLod * 0.25f) || (p.MaxZoomLod != 0 && zoom > p.MaxZoomLod * 0.25f))
                    { CulledByLod++; continue; }
                }
                into.Add(t);
            }
            return into.Count;
        }

        /// <summary>⭐ CE-1033 S5b — the position of the entity whose anchor is <paramref name="networkId"/> in the last triaged frame.</summary>
        public bool TryGetAnchor(long networkId, out Vector3 position)
        {
            if (_anchors.TryGetValue(networkId, out var a))
            {
                position = new Vector3(a.Primitive.AnchorWorldX, a.Primitive.AnchorWorldY, a.Primitive.AnchorWorldZ);
                return true;
            }
            position = default;
            return false;
        }

        /// <summary>The world point a triaged primitive stands for — its LOD point, and where a label is anchored.</summary>
        public static Vector3 PositionOf(in TriagedPrimitive3D t)
        {
            ref readonly var p = ref t.Primitive;
            switch (p.Shape)
            {
                case DebugPrimitiveShape.Line: return (p.LineStart + p.LineEnd) * 0.5f;
                case DebugPrimitiveShape.Arrow: return p.ArrowTo;
                case DebugPrimitiveShape.Sphere: return p.SphereCenter;
                case DebugPrimitiveShape.Box2D: return new Vector3(p.BoxCenterX, p.BoxCenterY, t.AnchorZ);
                case DebugPrimitiveShape.Text: return new Vector3(p.TextX, p.TextY, t.AnchorZ);
                case DebugPrimitiveShape.FilledTriangle:
                {
                    var c = (p.TriA + p.TriB + p.TriC) / 3f;
                    return new Vector3(c, t.AnchorZ);
                }
                case DebugPrimitiveShape.SemanticShape: return new Vector3(p.ResolvedWorldX, p.ResolvedWorldY, t.AnchorZ);
                case DebugPrimitiveShape.MilStd2525: return new Vector3(p.MilWorldPosX, p.MilWorldPosY, t.AnchorZ);
                default: return new Vector3(p.IconWorldPosX, p.IconWorldPosY, t.AnchorZ);
            }
        }

        private static void Resolve(ref DebugPrimitive p, in DebugPrimitive anchor, float yaw)
        {
            float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
            var origin = new Vector3(anchor.AnchorWorldX, anchor.AnchorWorldY, anchor.AnchorWorldZ);
            switch (p.Shape)
            {
                case DebugPrimitiveShape.Line:
                    p.LineStart = Apply(origin, cos, sin, p.LineStart);
                    p.LineEnd = Apply(origin, cos, sin, p.LineEnd);
                    break;
                case DebugPrimitiveShape.Arrow:
                    p.ArrowFrom = Apply(origin, cos, sin, p.ArrowFrom);
                    p.ArrowTo = Apply(origin, cos, sin, p.ArrowTo);
                    break;
                case DebugPrimitiveShape.Sphere:
                    p.SphereCenter = Apply(origin, cos, sin, p.SphereCenter);
                    break;
                case DebugPrimitiveShape.Box2D:
                {
                    var (x, y) = ApplyXY(origin, cos, sin, p.BoxCenterX, p.BoxCenterY);
                    p.BoxCenterX = x; p.BoxCenterY = y;
                    p.BoxAngleDeg += yaw * (180f / MathF.PI);
                    break;
                }
                case DebugPrimitiveShape.Text:
                {
                    var (x, y) = ApplyXY(origin, cos, sin, p.TextX, p.TextY);
                    p.TextX = x; p.TextY = y;
                    break;
                }
                case DebugPrimitiveShape.FilledTriangle:
                {
                    var (ax, ay) = ApplyXY(origin, cos, sin, p.TriA.X, p.TriA.Y);
                    var (bx, by) = ApplyXY(origin, cos, sin, p.TriB.X, p.TriB.Y);
                    var (cx, cy) = ApplyXY(origin, cos, sin, p.TriC.X, p.TriC.Y);
                    p.TriA = new Vector2(ax, ay); p.TriB = new Vector2(bx, by); p.TriC = new Vector2(cx, cy);
                    break;
                }
                case DebugPrimitiveShape.SemanticShape:
                    p.ResolvedWorldX = anchor.AnchorWorldX;
                    p.ResolvedWorldY = anchor.AnchorWorldY;
                    p.ResolvedYawRad = yaw;
                    p.ResolvedPitchRad = anchor.Pitch * DegToRad;
                    p.ResolvedRollRad = anchor.Roll * DegToRad;
                    break;
                case DebugPrimitiveShape.MilStd2525:
                {
                    var (x, y) = ApplyXY(origin, cos, sin, p.MilWorldPosX, p.MilWorldPosY);
                    p.MilWorldPosX = x; p.MilWorldPosY = y;
                    break;
                }
                case DebugPrimitiveShape.StructInspector:
                    break;   // a screen panel — its payload is not a position
                default:
                {
                    var (x, y) = ApplyXY(origin, cos, sin, p.IconWorldPosX, p.IconWorldPosY);
                    p.IconWorldPosX = x; p.IconWorldPosY = y;
                    break;
                }
            }
        }

        private static Vector3 Apply(Vector3 o, float cos, float sin, Vector3 local)
            => new Vector3(o.X + cos * local.X - sin * local.Y, o.Y + sin * local.X + cos * local.Y, o.Z + local.Z);

        private static (float x, float y) ApplyXY(Vector3 o, float cos, float sin, float lx, float ly)
            => (o.X + cos * lx - sin * ly, o.Y + sin * lx + cos * ly);
    }
}

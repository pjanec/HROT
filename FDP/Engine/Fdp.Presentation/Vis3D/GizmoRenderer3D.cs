using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>What <see cref="GizmoRenderer3D"/> draws with — HROT coordinates (X east, Y north, Z up), engine-free so the
    /// geometry is railable without a GL context. <see cref="RaylibGizmoSink3D"/> is the drawing one.</summary>
    public interface IGizmoSink3D
    {
        /// <summary>A line, its colour blended from <paramref name="ca"/> to <paramref name="cb"/>.</summary>
        void Line(Vector3 a, Vector3 b, Rgba32 ca, Rgba32 cb);

        /// <summary>A filled triangle, visible from both sides.</summary>
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Rgba32 colour);

        /// <summary>A sphere with real height (a detonation's fragment radius): wire, and translucent when <paramref name="fill"/> is set.</summary>
        void Sphere(Vector3 centre, float radius, Rgba32 colour, Rgba32 fill);
    }

    /// <summary>A gizmo label waiting for the overlay pass: where it stands, and the entity whose body top lifts it.</summary>
    public readonly record struct GizmoLabel3D(DebugPrimitive Primitive, Vector3 World, long AnchorId);

    /// <summary>
    /// ⭐⭐ CE-1033 S3 — the 3-D map's gizmo DRAWER: everything the shared <see cref="DebugPrimitiveTriage3D"/> kept, drawn in the
    /// 3-D scene by docs/DESIGN_Map_3D_Mode.md's rules:
    /// <list type="bullet">
    ///   <item><b>M18 — draping.</b> A world-space shape whose points all have <c>z = 0</c> (a pick box, an area, a measurement, a
    ///   route) lies on the GROUND at each vertex, lifted a little, and is SUBDIVIDED every <see cref="DrapeStep"/> metres so it
    ///   crosses a hill instead of cutting through it (R-248). A shape with height is drawn as given.</item>
    ///   <item><b>§3.9 group A — real height.</b> Fire traces, bursts, anchored shapes: drawn where they are. An un-anchored
    ///   sphere with height is a SPHERE (a fragment radius); a ground or anchored one is a horizontal ring (a selection ring).</item>
    ///   <item><b>M13 — labels.</b> Text, badges and icons are not drawn here: they are collected into <see cref="Labels"/> and
    ///   drawn by the overlay pass after the 3-D pass, at a constant pixel size, lifted to the top of their entity's body.</item>
    ///   <item><b>Sizes in pixels</b> (<see cref="SizeMode.ScreenPixels"/>) become metres at the shape's own distance (the
    ///   camera's <c>ZoomAt</c>), so a 6 px handle stays 6 px near and far.</item>
    /// </list>
    /// ⚠ Lines are one pixel wide — a width in metres (a road at its true width) is not honoured in 3-D yet.
    /// ⭐ What it cannot draw is COUNTED in <see cref="Skipped"/>, never silently dropped (DESIGN_Stride_Node_Modes.md §8):
    /// <c>SemanticShape</c> (the entity body layer draws bodies), <c>MilStd2525</c> (2-D symbology).
    /// </summary>
    public sealed class GizmoRenderer3D
    {
        /// <summary>The longest straight piece of a draped line or edge (m).</summary>
        public const float DrapeStep = 3f;

        /// <summary>The most pieces one draped edge is cut into.</summary>
        public const int MaxSegments = 160;

        private const int RingSegments = 40;

        /// <summary>The ground height (level 0) at (x, y) — the surface z = 0 shapes are draped on. Null ⇒ flat at 0.</summary>
        public Func<float, float, float>? GroundHeight { get; set; }

        /// <summary>What the last <see cref="Draw"/> could not draw, per shape.</summary>
        public ShapeCounters Skipped { get; } = new();

        /// <summary>Labels collected by the last <see cref="Draw"/>, for the overlay pass.</summary>
        public List<GizmoLabel3D> Labels { get; } = new();

        /// <summary>Panels (<c>StructInspector</c>) seen by the last <see cref="Draw"/> — screen panels, scheduled by the caller.</summary>
        public List<DebugPrimitive> Panels { get; } = new();

        /// <summary>Lines and triangles emitted in the last frame — the frame-cost counters (§7).</summary>
        public int LinesDrawn { get; private set; }
        /// <inheritdoc cref="LinesDrawn"/>
        public int TrianglesDrawn { get; private set; }

        private readonly List<Vector3> _path = new();
        private readonly List<Vector3> _rim = new();
        private Func<Vector3, float> _zoomAt = _ => 1f;
        private IGizmoSink3D _sink = null!;

        /// <summary>Draws <paramref name="primitives"/> (already triaged) through <paramref name="sink"/>.
        /// <paramref name="zoomAt"/> is the camera's pixels per metre at a world point.</summary>
        public void Draw(IReadOnlyList<TriagedPrimitive3D> primitives, Func<Vector3, float> zoomAt, IGizmoSink3D sink)
        {
            _zoomAt = zoomAt ?? throw new ArgumentNullException(nameof(zoomAt));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            Skipped.Reset();
            Labels.Clear();
            Panels.Clear();
            LinesDrawn = TrianglesDrawn = 0;

            for (int i = 0; i < primitives.Count; i++)
            {
                var t = primitives[i];
                ref readonly var p = ref t.Primitive;
                switch (p.Shape)
                {
                    case DebugPrimitiveShape.Line: DrawLine(in t); break;
                    case DebugPrimitiveShape.Arrow: DrawArrow(in t); break;
                    case DebugPrimitiveShape.Sphere: DrawSphere(in t); break;
                    case DebugPrimitiveShape.Box2D: DrawBox(in t); break;
                    case DebugPrimitiveShape.FilledTriangle: DrawFilledTriangle(in t); break;
                    case DebugPrimitiveShape.Text:
                    case DebugPrimitiveShape.EntityBadge:
                    case DebugPrimitiveShape.Icon:
                    {
                        var at = DebugPrimitiveTriage3D.PositionOf(in t);
                        if (!t.Anchored && at.Z == 0f) at.Z = Ground(at.X, at.Y);
                        Labels.Add(new GizmoLabel3D(p, at, t.Anchored ? t.AnchorId : 0));
                        break;
                    }
                    case DebugPrimitiveShape.StructInspector: Panels.Add(p); break;
                    default: Skipped.Add(p.Shape); break;
                }
            }
        }

        // ---- shapes -------------------------------------------------------------------------------------------

        private void DrawLine(in TriagedPrimitive3D t)
        {
            ref readonly var p = ref t.Primitive;
            bool drape = !t.Anchored && p.LineStart.Z == 0f && p.LineEnd.Z == 0f;
            _path.Clear();
            AppendEdge(_path, p.LineStart, p.LineEnd, drape, includeFirst: true);
            EmitPath(_path, p.Color, p.EndColor, p.LineStyle);
        }

        private void DrawArrow(in TriagedPrimitive3D t)
        {
            ref readonly var p = ref t.Primitive;
            bool drape = !t.Anchored && p.ArrowFrom.Z == 0f && p.ArrowTo.Z == 0f;
            _path.Clear();
            AppendEdge(_path, p.ArrowFrom, p.ArrowTo, drape, includeFirst: true);
            EmitPath(_path, p.Color, p.Color, LineStyle.Solid);
            if (_path.Count < 2) return;

            // The head: a flat triangle at the tip, its size in pixels as in 2-D.
            var tip = _path[^1];
            var dir = tip - _path[^2];
            dir.Z = 0f;
            if (dir.LengthSquared() < 1e-8f) return;
            dir = Vector3.Normalize(dir);
            float head = (p.ArrowHeadSize > 0f ? p.ArrowHeadSize : 8f) / MathF.Max(1e-3f, _zoomAt(tip));
            var side = new Vector3(-dir.Y, dir.X, 0f) * (head * 0.4f);
            var back = tip - dir * head;
            Tri(tip, back + side, back - side, p.Color);
        }

        private void DrawSphere(in TriagedPrimitive3D t)
        {
            ref readonly var p = ref t.Primitive;
            var c = p.SphereCenter;
            float radius = p.SizeMode == SizeMode.ScreenPixels ? p.SphereRadius / MathF.Max(1e-3f, _zoomAt(c)) : p.SphereRadius;
            if (radius <= 0f) return;
            bool hasFill = p.FillColor.A > 0;
            bool legacyFill = p.FillColor.A == 0 && p.ThicknessU16 == 0 && p.Color.A > 0;   // the 2-D renderer's rule
            var fill = hasFill ? p.FillColor : legacyFill ? p.Color : default;

            if (!t.Anchored && c.Z != 0f)
            {
                _sink.Sphere(c, radius, p.Color, fill);
                return;
            }

            bool drape = !t.Anchored;   // c.Z == 0 here
            _rim.Clear();
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * MathF.PI * 2f / RingSegments;
                _rim.Add(new Vector3(c.X + MathF.Cos(a) * radius, c.Y + MathF.Sin(a) * radius, c.Z));
            }
            var centre = drape ? Drape(c) : c;
            if (fill.A > 0)
            {
                for (int i = 0; i < RingSegments; i++)
                {
                    var a = drape ? Drape(_rim[i]) : _rim[i];
                    var b = drape ? Drape(_rim[(i + 1) % RingSegments]) : _rim[(i + 1) % RingSegments];
                    Tri(centre, a, b, fill);
                }
            }
            if (p.ThicknessU16 > 0 || !legacyFill)
                EmitClosed(_rim, drape, p.Color, p.LineStyle);
        }

        private void DrawBox(in TriagedPrimitive3D t)
        {
            ref readonly var p = ref t.Primitive;
            float z = t.Anchored ? t.AnchorZ : 0f;
            bool drape = !t.Anchored;
            var centre = new Vector3(p.BoxCenterX, p.BoxCenterY, z);
            float scale = p.SizeMode == SizeMode.ScreenPixels ? 1f / MathF.Max(1e-3f, _zoomAt(drape ? Drape(centre) : centre)) : 1f;
            float ex = p.BoxExtentX * scale, ey = p.BoxExtentY * scale;
            if (ex <= 0f && ey <= 0f) return;
            float ang = p.BoxAngleDeg * MathF.PI / 180f;
            var ax = new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0f);
            var ay = new Vector3(-MathF.Sin(ang), MathF.Cos(ang), 0f);

            bool hasFill = p.FillColor.A > 0;
            bool legacyFill = p.FillColor.A == 0 && p.ThicknessU16 == 0 && p.Color.A > 0;
            if (hasFill || legacyFill)
            {
                var fill = hasFill ? p.FillColor : p.Color;
                int nx = drape ? Math.Clamp((int)MathF.Ceiling(2f * ex / DrapeStep), 1, 32) : 1;
                int ny = drape ? Math.Clamp((int)MathF.Ceiling(2f * ey / DrapeStep), 1, 32) : 1;
                for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    Vector3 Corner(int u, int v) => centre + ax * (-ex + 2f * ex * u / nx) + ay * (-ey + 2f * ey * v / ny);
                    var a = Corner(i, j); var b = Corner(i + 1, j); var c = Corner(i + 1, j + 1); var d = Corner(i, j + 1);
                    if (drape) { a = Drape(a); b = Drape(b); c = Drape(c); d = Drape(d); }
                    Tri(a, b, c, fill);
                    Tri(a, c, d, fill);
                }
            }
            if (p.Color.A > 0 && (p.ThicknessU16 > 0 || !legacyFill))
            {
                _rim.Clear();
                _rim.Add(centre - ax * ex - ay * ey);
                _rim.Add(centre + ax * ex - ay * ey);
                _rim.Add(centre + ax * ex + ay * ey);
                _rim.Add(centre - ax * ex + ay * ey);
                EmitClosed(_rim, drape, p.Color, p.LineStyle);
            }
        }

        private void DrawFilledTriangle(in TriagedPrimitive3D t)
        {
            ref readonly var p = ref t.Primitive;
            float z = t.Anchored ? t.AnchorZ : 0f;
            var a = new Vector3(p.TriA, z); var b = new Vector3(p.TriB, z); var c = new Vector3(p.TriC, z);
            if (t.Anchored) { Tri(a, b, c, p.Color); return; }
            Subdivide(a, b, c, p.Color, 0);
        }

        private void Subdivide(Vector3 a, Vector3 b, Vector3 c, Rgba32 colour, int depth)
        {
            float longest = MathF.Max(Vector3.DistanceSquared(a, b), MathF.Max(Vector3.DistanceSquared(b, c), Vector3.DistanceSquared(c, a)));
            if (depth >= 5 || longest <= DrapeStep * DrapeStep * 4f)
            {
                Tri(Drape(a), Drape(b), Drape(c), colour);
                return;
            }
            var ab = (a + b) * 0.5f; var bc = (b + c) * 0.5f; var ca = (c + a) * 0.5f;
            Subdivide(a, ab, ca, colour, depth + 1);
            Subdivide(ab, b, bc, colour, depth + 1);
            Subdivide(ca, bc, c, colour, depth + 1);
            Subdivide(ab, bc, ca, colour, depth + 1);
        }

        // ---- draping -----------------------------------------------------------------------------------------

        /// <summary>A z = 0 point laid on the ground there, lifted by about half a pixel (at least 6 cm) so it does not fight the
        /// terrain's depth.</summary>
        public Vector3 Drape(Vector3 p)
        {
            float g = Ground(p.X, p.Y);
            var on = new Vector3(p.X, p.Y, g);
            float lift = MathF.Max(0.06f, 0.5f / MathF.Max(1e-3f, _zoomAt(on)));
            return new Vector3(p.X, p.Y, g + lift);
        }

        private float Ground(float x, float y) => GroundHeight?.Invoke(x, y) ?? 0f;

        /// <summary>Appends the edge a→b: subdivided and draped when <paramref name="drape"/>, else straight.</summary>
        private void AppendEdge(List<Vector3> into, Vector3 a, Vector3 b, bool drape, bool includeFirst)
        {
            if (!drape)
            {
                if (includeFirst) into.Add(a);
                into.Add(b);
                return;
            }
            float len = Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
            int n = Math.Clamp((int)MathF.Ceiling(len / DrapeStep), 1, MaxSegments);
            for (int i = includeFirst ? 0 : 1; i <= n; i++)
                into.Add(Drape(Vector3.Lerp(a, b, i / (float)n)));
        }

        private void EmitClosed(List<Vector3> corners, bool drape, Rgba32 colour, LineStyle style)
        {
            var corner = new List<Vector3>(corners);
            _path.Clear();
            for (int i = 0; i < corner.Count; i++)
                AppendEdge(_path, corner[i], corner[(i + 1) % corner.Count], drape, includeFirst: i == 0);
            EmitPath(_path, colour, colour, style);
        }

        /// <summary>Emits a polyline, its colour blended along its length; dashes and dots measured in PIXELS (8/8, 3/4 as in 2-D).</summary>
        private void EmitPath(List<Vector3> path, Rgba32 start, Rgba32 end, LineStyle style)
        {
            if (path.Count < 2) return;
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            if (total <= 0f) return;

            if (style == LineStyle.Solid)
            {
                float run = 0f;
                for (int i = 1; i < path.Count; i++)
                {
                    float seg = Vector3.Distance(path[i - 1], path[i]);
                    Seg(path[i - 1], path[i], Mix(start, end, run / total), Mix(start, end, (run + seg) / total));
                    run += seg;
                }
                return;
            }

            float mpp = 1f / MathF.Max(1e-3f, _zoomAt(path[path.Count / 2]));
            float on = (style == LineStyle.Dashed ? 8f : 3f) * mpp, off = (style == LineStyle.Dashed ? 8f : 4f) * mpp;
            float along = 0f, phase = 0f;
            bool drawing = true;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                float seg = Vector3.Distance(a, b);
                float s = 0f;
                while (s < seg)
                {
                    float limit = drawing ? on : off;
                    float step = MathF.Min(limit - phase, seg - s);
                    if (drawing)
                    {
                        var p0 = Vector3.Lerp(a, b, s / seg);
                        var p1 = Vector3.Lerp(a, b, (s + step) / seg);
                        Seg(p0, p1, Mix(start, end, (along + s) / total), Mix(start, end, (along + s + step) / total));
                    }
                    s += step;
                    phase += step;
                    if (phase >= limit - 1e-6f) { phase = 0f; drawing = !drawing; }
                }
                along += seg;
            }
        }

        private void Seg(Vector3 a, Vector3 b, Rgba32 ca, Rgba32 cb)
        {
            _sink.Line(a, b, ca, cb);
            LinesDrawn++;
        }

        private void Tri(Vector3 a, Vector3 b, Vector3 c, Rgba32 colour)
        {
            _sink.Triangle(a, b, c, colour);
            TrianglesDrawn++;
        }

        private static Rgba32 Mix(Rgba32 a, Rgba32 b, float t)
        {
            if (a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A) return a;
            t = Math.Clamp(t, 0f, 1f);
            return new Rgba32((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t),
                              (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
        }
    }
}

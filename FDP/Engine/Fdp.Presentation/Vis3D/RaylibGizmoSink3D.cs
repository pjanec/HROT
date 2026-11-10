using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>⭐ CE-1033 S3 — draws <see cref="GizmoRenderer3D"/>'s output with Raylib, inside the 3-D camera pass
    /// (HROT → Raylib by <see cref="HrotToRaylib"/>).
    /// ⭐ <c>CE-1196</c> (docs/DESIGN_Map_3D_Mode.md §6z CA-4) — the lines and fills are KEPT until <see cref="Flush"/>, which draws every
    /// fill first — with the flat program moved <see cref="MapCamera3D.GroundBiasSteps"/> depth steps toward the eye and WITHOUT writing
    /// depth — then the lines. ⛔ Was: each fill wrote depth at the same drape height as its neighbours, so two gizmos' overlapping discs
    /// fought pixel by pixel, and a fill lying on a hill fought the ground between its draped points. Now overlapping fills blend in the
    /// order drawn — the 2-D map's painter's rule — and the ground never shows through.</summary>
    public sealed class RaylibGizmoSink3D : IGizmoSink3D
    {
        /// <summary>The one instance (one 3-D pass at a time; <see cref="Flush"/> empties it).</summary>
        public static readonly RaylibGizmoSink3D Shared = new();

        private readonly List<(Vector3 A, Vector3 B, Color CA, Color CB)> _lines = new();
        private readonly List<(Vector3 A, Vector3 B, Vector3 C, Color Colour)> _fills = new();

        /// <summary>Fills and lines kept for the next <see cref="Flush"/>.</summary>
        public int PendingFills => _fills.Count;
        /// <inheritdoc cref="PendingFills"/>
        public int PendingLines => _lines.Count;

        public void Line(Vector3 a, Vector3 b, Rgba32 ca, Rgba32 cb)
            => _lines.Add((HrotToRaylib.Position(a), HrotToRaylib.Position(b), new Color(ca.R, ca.G, ca.B, ca.A), new Color(cb.R, cb.G, cb.B, cb.A)));

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Rgba32 colour)
            => _fills.Add((HrotToRaylib.Position(a), HrotToRaylib.Position(b), HrotToRaylib.Position(c), new Color(colour.R, colour.G, colour.B, colour.A)));

        /// <summary>⭐ <c>CE-1196</c> — draws what was kept (inside the 3-D camera pass): the fills, biased toward the eye with depth writes
        /// off, then the lines; and forgets them.</summary>
        public void Flush()
        {
            if (_fills.Count > 0)
            {
                Rlgl.DrawRenderBatchActive();
                var lit = LitShader.Shared;
                if (lit.FlatIsValid)
                {
                    Raylib.BeginShaderMode(lit.Flat);
                    lit.SetFlatDepthBias(MapCamera3D.GroundBiasNdc);
                }
                Rlgl.DisableDepthMask();
                foreach (var (a, b, c, colour) in _fills)
                {
                    // ⭐ Both windings: whether a world triangle faces the camera depends on the view; the culled one costs nothing.
                    Raylib.DrawTriangle3D(a, b, c, colour);
                    Raylib.DrawTriangle3D(a, c, b, colour);
                }
                Rlgl.DrawRenderBatchActive();
                Rlgl.EnableDepthMask();
                if (lit.FlatIsValid) Raylib.EndShaderMode();
            }
            foreach (var (a, b, ca, cb) in _lines)
            {
                Rlgl.Begin(DrawMode.Lines);
                Rlgl.Color4ub(ca.R, ca.G, ca.B, ca.A); Rlgl.Vertex3f(a.X, a.Y, a.Z);
                Rlgl.Color4ub(cb.R, cb.G, cb.B, cb.A); Rlgl.Vertex3f(b.X, b.Y, b.Z);
                Rlgl.End();
            }
            _fills.Clear();
            _lines.Clear();
        }

        public void Sphere(Vector3 centre, float radius, Rgba32 colour, Rgba32 fill)
        {
            var c = HrotToRaylib.Position(centre);
            if (fill.A > 0) Raylib.DrawSphereEx(c, radius, SphereRings, SphereSlices, new Color(fill.R, fill.G, fill.B, fill.A));
            if (colour.A > 0) DrawWireSphere(c, radius, new Color(colour.R, colour.G, colour.B, colour.A));
        }

        /// <summary>The sphere's tessellation (unchanged since CE-1033).</summary>
        public const int SphereRings = 10, SphereSlices = 16;

        // ⭐ CE-4388 — the wire sphere from a CACHED mesh. 📐 Measured on Windows (scale-1 + 1 000 men, 3-D): 240 route-waypoint spheres took 15.9 ms a frame
        //   — raylib's DrawSphereWires computes ≈ 4 trig calls per vertex, ≈ 4 600 per sphere, every frame (its DrawSphereEx fill is already optimised). The SAME
        //   segments (raylib 5.5's own formula, rmodels.c DrawSphereWires) are built ONCE as a unit mesh of degenerate triangles (A, B, B) — in wireframe mode
        //   each rasterises exactly the A–B line — and each sphere is one draw call, scaled and moved. Same lines, same colour, unlit.
        private Mesh _wireSphere;
        private Material _wireMaterial;
        private bool _hasWireSphere;
        private IntPtr _wireSphereWindow;   // the window (GL context) the mesh lives in

        /// <summary>The unit wire sphere's segments, as raylib 5.5's DrawSphereWires emits them (pairs of points, raylib space, radius 1).</summary>
        public static Vector3[] WireSphereSegments(int rings, int slices)
        {
            var pts = new List<Vector3>((rings + 2) * slices * 6);
            const float Deg = MathF.PI / 180f;
            Vector3 P(int i, int j) => new(
                MathF.Cos(Deg * (270 + (180.0f / (rings + 1)) * i)) * MathF.Sin(Deg * (360.0f * j / slices)),
                MathF.Sin(Deg * (270 + (180.0f / (rings + 1)) * i)),
                MathF.Cos(Deg * (270 + (180.0f / (rings + 1)) * i)) * MathF.Cos(Deg * (360.0f * j / slices)));
            for (int i = 0; i < rings + 2; i++)
                for (int j = 0; j < slices; j++)
                {
                    pts.Add(P(i, j)); pts.Add(P(i + 1, j + 1));
                    pts.Add(P(i + 1, j + 1)); pts.Add(P(i + 1, j));
                    pts.Add(P(i + 1, j)); pts.Add(P(i, j));
                }
            return pts.ToArray();
        }

        private unsafe void EnsureWireSphere()
        {
            // ⭐ the mesh lives in ONE GL context: a new window (measured: a rail session after another on Windows, each with its own window) has a new
            //   context in which the old mesh draws nothing — rebuild for it (the old context, and its mesh, are gone with their window)
            var window = (IntPtr)Raylib.GetWindowHandle();
            if (_hasWireSphere && window == _wireSphereWindow) return;
            _wireSphereWindow = window;
            var seg = WireSphereSegments(SphereRings, SphereSlices);
            int tris = seg.Length / 2;
            var mesh = new Mesh(tris * 3, tris);
            mesh.AllocVertices();
            var v = mesh.VerticesAs<Vector3>();
            // (A, B, B + ε): GL discards a ZERO-area triangle before rasterising, even in wireframe (measured: nothing drawn) — a hair-thin one draws
            //   its long edges onto the A–B line
            for (int k = 0; k < tris; k++)
            {
                var a = seg[k * 2]; var b = seg[k * 2 + 1];
                var d = b - a;
                var side = Vector3.Cross(d, MathF.Abs(d.Y) < 0.9f * d.Length() ? Vector3.UnitY : Vector3.UnitX);
                side = side.LengthSquared() > 0f ? Vector3.Normalize(side) * 1e-4f : new Vector3(1e-4f, 0f, 0f);
                v[k * 3] = a; v[k * 3 + 1] = b; v[k * 3 + 2] = b + side;
            }
            Raylib.UploadMesh(ref mesh, false);
            _wireSphere = mesh;
            _wireMaterial = Raylib.LoadMaterialDefault();
            _hasWireSphere = true;
        }

        private unsafe void DrawWireSphere(Vector3 centreRaylib, float radius, Color colour)
        {
            EnsureWireSphere();
            _wireMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = colour;
            var transform = Raymath.MatrixMultiply(Raymath.MatrixScale(radius, radius, radius), Raymath.MatrixTranslate(centreRaylib.X, centreRaylib.Y, centreRaylib.Z));
            Rlgl.DisableBackfaceCulling();   // a hair-thin triangle may face away
            Rlgl.EnableWireMode();
            Raylib.DrawMesh(_wireSphere, _wireMaterial, transform);
            Rlgl.DisableWireMode();
            Rlgl.EnableBackfaceCulling();
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1033 S3 (M13) — the label overlay: gizmo text, badges and icons drawn AFTER the 3-D pass, at their projected screen
    /// point, at the same constant pixel size the 2-D map uses (<c>GizmoMap.Presentation.GizmoTextDraw</c> — one text renderer).
    /// An anchored label is lifted to the top of its entity's drawn body (<paramref name="liftOf"/>), so a name sits on the
    /// tank, not inside it. Drawn on top of everything (name-tag style, M13); a label behind the camera is not drawn.
    /// </summary>
    public static class LabelOverlay3D
    {
        /// <summary>The lift for an anchored label whose body is unknown (m).</summary>
        public const float DefaultLift = 2f;

        /// <summary>Where each label lands on screen — railable without a GL context. Off-screen ⇒ <c>null</c>.</summary>
        public static Vector2? ScreenOf(in GizmoLabel3D label, MapCamera3D camera, System.Func<long, float?>? liftOf)
        {
            var world = label.World;
            if (label.AnchorId != 0) world.Z += liftOf?.Invoke(label.AnchorId) ?? DefaultLift;
            var s = camera.WorldToScreen(world);
            return s.X < -1e5f ? null : s;
        }

        /// <summary>Draws <paramref name="labels"/> (outside the 3-D camera pass).</summary>
        public static int Draw(IReadOnlyList<GizmoLabel3D> labels, MapCamera3D camera, System.Func<long, float?>? liftOf)
        {
            int drawn = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (ScreenOf(in label, camera, liftOf) is not { } s) continue;
                var p = label.Primitive;
                var colour = new Color(p.Color.R, p.Color.G, p.Color.B, p.Color.A);
                switch (p.Shape)
                {
                    case DebugPrimitiveShape.Text:
                        GizmoMap.Presentation.GizmoTextDraw.DrawScreen(in p, s, colour);
                        break;
                    case DebugPrimitiveShape.EntityBadge:
                    {
                        var rich = p.BadgeRichText;
                        GizmoMap.Presentation.RichTextRenderer.DrawRichTextBadge(ref rich, (int)s.X, (int)s.Y, 12);
                        break;
                    }
                    default:
                        Raylib.DrawCircleV(s, 4f, Color.Yellow);   // the 2-D renderer's Icon fallback
                        break;
                }
                drawn++;
            }
            return drawn;
        }
    }
}

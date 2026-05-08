using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    /// <summary>
    /// Raylib-based renderer that iterates a span of <see cref="DebugPrimitive"/> and issues
    /// draw calls. Filtering, LOD culling and painter's-algorithm sorting are done in
    /// <see cref="Render"/>; actual draw calls are in <see cref="DispatchShape"/> so that
    /// test subclasses can override the latter to capture dispatched primitives without
    /// invoking Raylib.
    /// </summary>
    public class DebugPrimitiveRenderer2D
    {
        private ushort _activeLayerMask = 0xFFFF; // All 16 debug layers visible by default.
        protected readonly ISimulationView? _view;

        /// <summary>Current camera; must be set before each Render() call by the hosting layer.</summary>
        public Camera2D Camera { get; set; }

        public DebugPrimitiveRenderer2D(ISimulationView? view = null)
        {
            _view = view;
        }

        /// <summary>Overrides the 16-bit layer visibility mask.</summary>
        public void SetLayerMask(ushort mask) => _activeLayerMask = mask;

        /// <summary>
        /// Filters, resolves coordinate spaces, sorts by (DebugLayer, ZIndex) and dispatches
        /// each surviving primitive to <see cref="DispatchShape"/>.
        /// </summary>
        public void Render(ReadOnlySpan<DebugPrimitive> primitives, RenderContext ctx)
        {
            float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
            var sortBuffer = new List<DebugPrimitive>(primitives.Length);

            foreach (ref readonly var prim in primitives)
            {
                // Filter: must target the 2-D map pipeline.
                if ((prim.TargetView & PipelineTarget.Map2D) == 0) continue;

                // Filter: layer mask (only layers 0-15 are valid).
                if (prim.DebugLayer >= 16 || (_activeLayerMask & (1u << prim.DebugLayer)) == 0)
                    continue;

                // Filter: LOD zoom culling.
                if (prim.MinZoomLod != 0 && zoom < prim.MinZoomLod * 0.25f) continue;
                if (prim.MaxZoomLod != 0 && zoom > prim.MaxZoomLod * 0.25f) continue;

                // Coordinate-space resolution for EntityLocal.
                if (prim.Space == CoordinateSpace.EntityLocal)
                {
                    var anchor = prim.GetAnchor();
                    if (_view == null
                        || !_view.IsAlive(anchor)
                        || !_view.HasComponent<SimTransform>(anchor))
                        continue; // Unresolvable: skip.

                    ref readonly var tf = ref _view.GetComponentRO<SimTransform>(anchor);
                    DebugPrimitive resolved = prim;

                    switch (prim.Shape)
                    {
                        case DebugPrimitiveShape.Line:
                            resolved.LineStart = ApplyTransform(in tf, prim.LineStart);
                            resolved.LineEnd   = ApplyTransform(in tf, prim.LineEnd);
                            break;
                        case DebugPrimitiveShape.Arrow:
                            resolved.ArrowFrom = ApplyTransform(in tf, prim.ArrowFrom);
                            resolved.ArrowTo   = ApplyTransform(in tf, prim.ArrowTo);
                            break;
                        case DebugPrimitiveShape.Sphere:
                        {
                            resolved.SphereCenter = ApplyTransform(in tf, prim.SphereCenter);
                            break;
                        }
                        case DebugPrimitiveShape.Box2D:
                        {
                            var c = ApplyTransform2D(in tf, prim.BoxCenterX, prim.BoxCenterY);
                            resolved.BoxCenterX  = c.X;
                            resolved.BoxCenterY  = c.Y;
                            resolved.BoxAngleDeg = prim.BoxAngleDeg + RotationDegrees2D(in tf);
                            break;
                        }
                        case DebugPrimitiveShape.Text:
                        {
                            var c = ApplyTransform2D(in tf, prim.TextX, prim.TextY);
                            resolved.TextX = c.X;
                            resolved.TextY = c.Y;
                            break;
                        }
                        default:
                        {
                            // Icon and other shapes: transform the payload origin (IconWorldPos).
                            var c = ApplyTransform2D(in tf, prim.IconWorldPosX, prim.IconWorldPosY);
                            resolved.IconWorldPosX = c.X;
                            resolved.IconWorldPosY = c.Y;
                            break;
                        }
                    }

                    resolved.Space = CoordinateSpace.World;
                    sortBuffer.Add(resolved);
                    continue;
                }

                sortBuffer.Add(prim);
            }

            // Stable painter's-algorithm sort: DebugLayer ascending, ZIndex ascending.
            sortBuffer.Sort(static (a, b) =>
            {
                int layerCmp = a.DebugLayer.CompareTo(b.DebugLayer);
                return layerCmp != 0 ? layerCmp : a.ZIndex.CompareTo(b.ZIndex);
            });

            foreach (var prim in sortBuffer)
                DispatchShape(in prim, ctx);
        }

        /// <summary>
        /// Issues the actual Raylib draw call(s) for one primitive.
        /// Override in test subclasses to capture dispatches without Raylib.
        /// </summary>
        protected virtual void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
        {
            float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
            var color = ToRaylibColor(prim.Color);

            float thickness = prim.SizeMode == SizeMode.ScreenPixels
                ? prim.Thickness / zoom
                : prim.Thickness;

            // Geometric dimensions (radius, head size, extents) scale inversely with zoom for
            // SizeMode.ScreenPixels so they remain constant on screen regardless of camera zoom.
            float geomScale = prim.SizeMode == SizeMode.ScreenPixels ? 1f / zoom : 1f;

            // Screen-space primitives: bracket with EndMode2D / BeginMode2D.
            bool screenSpace = prim.Space == CoordinateSpace.Screen;
            if (screenSpace) Raylib.EndMode2D();

            switch (prim.Shape)
            {
                case DebugPrimitiveShape.Line:
                {
                    var startPos = new Vector2(prim.LineStart.X, prim.LineStart.Y);
                    var endPos   = new Vector2(prim.LineEnd.X,   prim.LineEnd.Y);

                    // Gradient line: start color != end color.
                    if (prim.EndColor.R != prim.Color.R
                        || prim.EndColor.G != prim.Color.G
                        || prim.EndColor.B != prim.Color.B
                        || prim.EndColor.A != prim.Color.A)
                    {
                        DrawGradientLine(startPos, endPos, thickness, color, ToRaylibColor(prim.EndColor));
                    }
                    else
                    {
                        Raylib.DrawLineEx(startPos, endPos, thickness, color);
                    }
                    break;
                }

                case DebugPrimitiveShape.Sphere:
                {
                    var center = new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y);
                    Raylib.DrawCircleV(center, prim.SphereRadius * geomScale, color);
                    break;
                }

                case DebugPrimitiveShape.Arrow:
                {
                    var from = new Vector2(prim.ArrowFrom.X, prim.ArrowFrom.Y);
                    var to   = new Vector2(prim.ArrowTo.X,   prim.ArrowTo.Y);
                    DrawArrow(from, to, prim.ArrowHeadSize * geomScale, color, thickness);
                    break;
                }

                case DebugPrimitiveShape.Box2D:
                {
                    var rect = new Rectangle(
                        prim.BoxCenterX - prim.BoxExtentX * geomScale,
                        prim.BoxCenterY - prim.BoxExtentY * geomScale,
                        prim.BoxExtentX * 2f * geomScale,
                        prim.BoxExtentY * 2f * geomScale);
                    var origin = new Vector2(prim.BoxExtentX * geomScale, prim.BoxExtentY * geomScale);
                    Raylib.DrawRectanglePro(rect, origin, prim.BoxAngleDeg, color);
                    break;
                }

                case DebugPrimitiveShape.Text:
                {
                    int tx = (int)prim.TextX;
                    int ty = (int)prim.TextY;
                    string str = prim.TextContent.ToString();
                    Raylib.DrawText(str, tx, ty, 12, color);
                    break;
                }

                case DebugPrimitiveShape.EntityBadge:
                    DrawEntityBadge(in prim, ctx);
                    break;

                default:
                    // Unknown / unsupported shape: silently skip.
                    break;
            }

            if (screenSpace) Raylib.BeginMode2D(Camera);
        }

        // ---- Private helpers ------------------------------------------------

        private static void DrawGradientLine(
            Vector2 from, Vector2 to,
            float thickness,
            Color startColor, Color endColor)
        {
            var dir  = to - from;
            float len = dir.Length();
            if (len < float.Epsilon) return;

            var unit  = dir / len;
            var perp  = new Vector2(-unit.Y, unit.X) * (thickness * 0.5f);

            // Four vertices of the quad.
            var v0 = from + perp;
            var v1 = from - perp;
            var v2 = to   - perp;
            var v3 = to   + perp;

            Rlgl.Begin((int)DrawMode.Quads);
            Rlgl.Color4ub(startColor.R, startColor.G, startColor.B, startColor.A); Rlgl.Vertex2f(v0.X, v0.Y);
            Rlgl.Color4ub(startColor.R, startColor.G, startColor.B, startColor.A); Rlgl.Vertex2f(v1.X, v1.Y);
            Rlgl.Color4ub(endColor.R,   endColor.G,   endColor.B,   endColor.A);   Rlgl.Vertex2f(v2.X, v2.Y);
            Rlgl.Color4ub(endColor.R,   endColor.G,   endColor.B,   endColor.A);   Rlgl.Vertex2f(v3.X, v3.Y);
            Rlgl.End();
        }

        private static void DrawArrow(
            Vector2 from, Vector2 to,
            float headSize, Color color, float thickness)
        {
            Raylib.DrawLineEx(from, to, thickness, color);

            var dir  = to - from;
            float len = dir.Length();
            if (len < float.Epsilon) return;

            var unit = dir / len;
            var perp = new Vector2(-unit.Y, unit.X);

            var tip   = to;
            var baseL = to - unit * headSize + perp * (headSize * 0.4f);
            var baseR = to - unit * headSize - perp * (headSize * 0.4f);

            Raylib.DrawTriangle(tip, baseL, baseR, color);
        }

        private void DrawEntityBadge(in DebugPrimitive prim, RenderContext ctx)
        {
            if (_view == null) return;
            var badgeEntity = new Entity(prim.BadgeTargetIndex, prim.BadgeTargetGen);
            if (!_view.IsAlive(badgeEntity) || !_view.HasComponent<SimTransform>(badgeEntity))
                return; // Silently skip when no transform available.

            ref readonly var tf = ref _view.GetComponentRO<SimTransform>(badgeEntity);
            var worldPos  = new Vector2(tf.Position.X, tf.Position.Y);
            var screenPos = Raylib.GetWorldToScreen2D(worldPos, Camera);

            var richText = prim.BadgeRichText;
            // GizmoMap.Contracts.FixedString32 and Fdp.Core.FixedString32 share identical
            // 32-byte sequential layout; reinterpret to satisfy the renderer signature.
            ref var richTextCore = ref Unsafe.As<Fdp.Toolkit.Diagnostics.Gizmos.FixedString32, Fdp.Core.FixedString32>(ref richText);
            RichTextRenderer.DrawRichTextBadge(ref richTextCore, (int)screenPos.X, (int)screenPos.Y, 12);
        }

        /// <summary>Converts an <see cref="Rgba32"/> to a Raylib <see cref="Color"/>.</summary>
        protected static Color ToRaylibColor(Rgba32 c)
            => new Color(c.R, c.G, c.B, c.A);

        // ---- EntityLocal transform helpers ----------------------------------

        private static Vector3 ApplyTransform(in SimTransform tf, Vector3 local)
            => tf.Position + Vector3.Transform(local, tf.Rotation);

        private static Vector2 ApplyTransform2D(in SimTransform tf, float localX, float localY)
        {
            var world = ApplyTransform(in tf, new Vector3(localX, localY, 0f));
            return new Vector2(world.X, world.Y);
        }

        private static float RotationDegrees2D(in SimTransform tf)
        {
            var q = tf.Rotation;
            return MathF.Atan2(
                2f * (q.W * q.Z + q.X * q.Y),
                1f - 2f * (q.Y * q.Y + q.Z * q.Z)
            ) * (180f / MathF.PI);
        }
    }
}
